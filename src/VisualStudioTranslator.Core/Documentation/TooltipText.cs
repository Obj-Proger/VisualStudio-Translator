using System.Text;
using System.Text.RegularExpressions;

namespace VisualStudioTranslator.Core.Documentation;

/// <summary>One piece of a line in a tooltip: text a person wrote, or code, which stays as it is.</summary>
public sealed record TooltipRun(string Text, bool IsCode);

/// <summary>
/// One piece of a translated tooltip line: translated text, or a stretch of the original line's code runs, given by
/// the first run and how many, so it is drawn exactly as before.
/// </summary>
public sealed record TranslatedPiece(string Text, int FirstCodeRun = -1, int CodeRunCount = 0)
{
    public bool IsCode => FirstCodeRun >= 0;
}

/// <summary>
/// The lines of a tooltip, whoever drew them and whatever the programming language, as a document the translation
/// pipeline takes, and the translation back as lines.
/// <list type="bullet">
/// <item>Each line is one paragraph. Runs of code next to each other are one code span, which the pipeline keeps
/// whole, so "LanguageTag.Normalize(string)" stays one name rather than six.</item>
/// <item>A name quoted in the text, as compilers write them ('value', `Count`), and a web address are code too, so the
/// translation can neither translate nor break them.</item>
/// <item>A line break inside text is where the source comment happened to wrap, not where the translation should:
/// it is sent as a space. An empty line between two pieces of text stays.</item>
/// <item>A line is worth translating only when its text holds at least two words: a signature, a caption such as
/// "Returns:" or a lone name is not prose.</item>
/// </list>
/// </summary>
public static class TooltipText
{
    private static readonly Regex Quoted = new(@"https?://[^\s)\]>]+|'[^'\s]{1,80}'|`[^`\s]{1,80}`", RegexOptions.Compiled);
    private static readonly Regex EmptyLine = new(@"\r?\n[ \t]*\r?\n", RegexOptions.Compiled);
    private static readonly Regex LineBreak = new(@"[ \t]*\r?\n[ \t]*", RegexOptions.Compiled);
    private static readonly Regex Word = new(@"\p{L}{2,}", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Whether the line holds prose to translate.</summary>
    public static bool IsProse(IReadOnlyList<TooltipRun> line)
    {
        string text = string.Concat(line.Where(run => !run.IsCode).Select(run => run.Text));
        return Word.Matches(Quoted.Replace(text, " ")).Count >= 2;
    }

    /// <summary>The lines as the paragraphs of a document's summary.</summary>
    public static DocumentModel ToDocument(IReadOnlyList<IReadOnlyList<TooltipRun>> lines)
    {
        List<Block> paragraphs = [];

        foreach (IReadOnlyList<TooltipRun> line in lines)
        {
            List<Inline> inlines = [];

            foreach (Piece piece in Pieces(line))
            {
                if (piece.IsCode)
                {
                    inlines.Add(new CodeSpan { Text = piece.Text });
                }
                else
                {
                    AddProse(inlines, piece.Text);
                }
            }

            paragraphs.Add(new Paragraph { Inlines = inlines });
        }

        return new DocumentModel { Summary = paragraphs.Count > 0 ? new Section { Blocks = paragraphs } : null };
    }

    /// <summary>
    /// The translated lines, one for each line given, in the same order. A line the document has no paragraph for
    /// is <see langword="null"/>: it was not translated and keeps its original.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<TranslatedPiece>?> FromDocument(
        IReadOnlyList<IReadOnlyList<TooltipRun>> lines, DocumentModel translated)
    {
        IReadOnlyList<Block> blocks = translated.Summary?.Blocks ?? [];
        List<IReadOnlyList<TranslatedPiece>?> result = [];

        for (int index = 0; index < lines.Count; index++)
        {
            if (index >= blocks.Count || blocks[index] is not Paragraph paragraph)
            {
                result.Add(null);
                continue;
            }

            List<Piece> groups = [.. Pieces(lines[index]).Where(piece => piece.IsCode && piece.FirstRun >= 0)];
            bool[] used = new bool[groups.Count];
            List<TranslatedPiece> pieces = [];

            foreach (Inline inline in paragraph.Inlines)
            {
                if (inline is CodeSpan code)
                {
                    int match = FindGroup(groups, used, code.Text);

                    if (match >= 0)
                    {
                        used[match] = true;
                        pieces.Add(new TranslatedPiece(groups[match].Text, groups[match].FirstRun, groups[match].RunCount));
                    }
                    else
                    {
                        pieces.Add(new TranslatedPiece(code.Text));
                    }
                }
                else
                {
                    pieces.Add(new TranslatedPiece(TextOf(inline)));
                }
            }

            result.Add(pieces);
        }

        return result;
    }

    // Prose with quoted names and web addresses taken out as code, and wrapped lines joined.
    private static void AddProse(List<Inline> inlines, string text)
    {
        text = string.Join("\n\n", EmptyLine.Split(text).Select(part => LineBreak.Replace(part, " ")));
        int at = 0;

        foreach (Match quoted in Quoted.Matches(text))
        {
            if (quoted.Index > at)
            {
                inlines.Add(new TextRun { Text = text.Substring(at, quoted.Index - at) });
            }

            inlines.Add(new CodeSpan { Text = quoted.Value });
            at = quoted.Index + quoted.Length;
        }

        if (at < text.Length)
        {
            inlines.Add(new TextRun { Text = text.Substring(at) });
        }
    }

    private static int FindGroup(List<Piece> groups, bool[] used, string text)
    {
        string wanted = Normalize(text);

        for (int index = 0; index < groups.Count; index++)
        {
            if (!used[index] && Normalize(groups[index].Text) == wanted)
            {
                return index;
            }
        }

        return -1;
    }

    private static string Normalize(string text) => Whitespace.Replace(text, " ").Trim();

    private static string TextOf(Inline inline) => inline switch
    {
        TextRun run => run.Text,
        CodeSpan code => code.Text,
        EmphasisRun emphasis => string.Concat(emphasis.Content.Select(TextOf)),
        Ref reference => reference.DisplayContent is { } display ? string.Concat(display.Select(TextOf)) : reference.Target,
        _ => string.Empty,
    };

    // The line as prose and stretches of code. Space between two code runs belongs to the stretch; space next to
    // prose is prose.
    private static List<Piece> Pieces(IReadOnlyList<TooltipRun> line)
    {
        List<Piece> pieces = [];
        StringBuilder text = new();
        int first = -1;
        int count = 0;
        bool code = false;

        for (int index = 0; index < line.Count; index++)
        {
            TooltipRun run = line[index];
            bool blank = string.IsNullOrWhiteSpace(run.Text);
            // Space on its own is never code, whatever colour it has: a line break coloured as whitespace between two
            // words is part of the sentence, and would otherwise come back as a break in the middle of the translation.
            bool isCode = blank ? code && NextIsCode(line, index) : run.IsCode;

            if (isCode != code && text.Length > 0)
            {
                pieces.Add(new Piece(text.ToString(), code, code ? first : -1, code ? count : 0));
                text.Clear();
                count = 0;
            }

            if (isCode && count == 0)
            {
                first = index;
            }

            code = isCode;
            text.Append(run.Text);
            count += isCode ? 1 : 0;
        }

        if (text.Length > 0)
        {
            pieces.Add(new Piece(text.ToString(), code, code ? first : -1, code ? count : 0));
        }

        return pieces;
    }

    private static bool NextIsCode(IReadOnlyList<TooltipRun> line, int index)
    {
        for (int next = index + 1; next < line.Count; next++)
        {
            if (!string.IsNullOrWhiteSpace(line[next].Text))
            {
                return line[next].IsCode;
            }
        }

        return false;
    }

    private sealed record Piece(string Text, bool IsCode, int FirstRun, int RunCount);
}