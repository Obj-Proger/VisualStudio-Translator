using System.Text;
using System.Text.RegularExpressions;

namespace VisualStudioTranslator.Core.Documentation;

/// <summary>
/// Plain text taken apart into translation units, and how to put it back together. Each unit is one paragraph of the
/// <see cref="DocumentModel.Summary"/>, so the text takes the same way through the pipeline as documentation does,
/// cache and checks included.
/// </summary>
/// <param name="Document">The units, as paragraphs of the summary.</param>
/// <param name="Prefixes">
/// What goes before each unit: line breaks, a list marker, or a space between pieces of a long paragraph.
/// </param>
public sealed record PlainTextDocument(DocumentModel Document, IReadOnlyList<string> Prefixes);

/// <summary>
/// Turns plain text, such as a comment selected in the editor or what the user typed into the translator window, into
/// a <see cref="PlainTextDocument"/>, and the translated document back into text laid out like the original.
/// <list type="bullet">
/// <item>Paragraphs are separated by blank lines. The lines of one paragraph are joined: in a comment they were
/// wrapped to fit the editor, not to say anything.</item>
/// <item>A line that begins a list item (<c>-</c>, <c>*</c>, <c>•</c>, <c>1.</c>) starts a unit of its own, and its
/// marker stays out of the text the model sees.</item>
/// <item>A paragraph longer than <see cref="MaxUnitLength"/> is cut between sentences, so no unit is too long for a
/// provider to take.</item>
/// </list>
/// </summary>
public static class PlainText
{
    /// <summary>The longest unit made; well within what the local model is given at once.</summary>
    public const int MaxUnitLength = 1000;

    private static readonly Regex ListMarker = new(@"^([-*•]|\d{1,3}[.)])\s+", RegexOptions.Compiled);

    public static PlainTextDocument Parse(string text)
    {
        List<string> units = [];
        List<string> prefixes = [];
        StringBuilder paragraph = new();
        string pendingPrefix = string.Empty;
        string pendingBreak = string.Empty;

        foreach (string rawLine in (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = rawLine.Trim();

            if (line.Length == 0)
            {
                Flush();
                pendingBreak = units.Count > 0 ? "\n\n" : string.Empty;
                continue;
            }

            Match marker = ListMarker.Match(line);

            if (marker.Success)
            {
                Flush();
                pendingPrefix = (pendingBreak.Length > 0 ? pendingBreak : units.Count > 0 ? "\n" : string.Empty) + marker.Value;
                pendingBreak = string.Empty;
                paragraph.Append(line, marker.Length, line.Length - marker.Length);
                continue;
            }

            if (paragraph.Length > 0)
            {
                paragraph.Append(' ');
            }
            else
            {
                pendingPrefix += pendingBreak;
                pendingBreak = string.Empty;
            }

            paragraph.Append(line);
        }

        Flush();

        Section summary = new() { Blocks = [.. units.Select(unit => new Paragraph { Inlines = [new TextRun { Text = unit }] })] };

        return new PlainTextDocument(new DocumentModel { Summary = units.Count > 0 ? summary : null }, prefixes);

        void Flush()
        {
            if (paragraph.Length == 0)
            {
                return;
            }

            bool first = true;

            foreach (string piece in Split(paragraph.ToString()))
            {
                prefixes.Add(first ? pendingPrefix : " ");
                units.Add(piece);
                first = false;
            }

            paragraph.Clear();
            pendingPrefix = string.Empty;
        }
    }

    /// <summary>
    /// The text of <paramref name="translated"/>, which must be <paramref name="layout"/>'s document or a translation of it.
    /// </summary>
    public static string Write(PlainTextDocument layout, DocumentModel translated)
    {
        IReadOnlyList<Block> blocks = translated.Summary?.Blocks ?? [];
        StringBuilder text = new();

        for (int index = 0; index < blocks.Count && index < layout.Prefixes.Count; index++)
        {
            text.Append(layout.Prefixes[index]);

            if (blocks[index] is Paragraph paragraph)
            {
                AppendInlines(text, paragraph.Inlines);
            }
        }

        return text.ToString();
    }

    private static void AppendInlines(StringBuilder text, IReadOnlyList<Inline> inlines)
    {
        foreach (Inline inline in inlines)
        {
            switch (inline)
            {
                case TextRun run:
                    text.Append(run.Text);
                    break;
                case CodeSpan code:
                    text.Append(code.Text);
                    break;
                case EmphasisRun emphasis:
                    AppendInlines(text, emphasis.Content);
                    break;
                case Ref reference:
                    if (reference.DisplayContent is { } display)
                    {
                        AppendInlines(text, display);
                    }
                    else
                    {
                        text.Append(reference.Target);
                    }

                    break;
            }
        }
    }

    // Pieces no longer than the limit, cut after a sentence where there is one, else at a space, else anywhere.
    private static IEnumerable<string> Split(string paragraph)
    {
        string rest = paragraph;

        while (rest.Length > MaxUnitLength)
        {
            int cut = LastSentenceEnd(rest, MaxUnitLength);

            if (cut <= 0)
            {
                cut = rest.LastIndexOf(' ', MaxUnitLength);
            }

            if (cut <= 0)
            {
                cut = MaxUnitLength;
            }

            yield return rest.Substring(0, cut).Trim();
            rest = rest.Substring(cut).TrimStart();
        }

        if (rest.Length > 0)
        {
            yield return rest;
        }
    }

    // Just past the last sentence end that leaves the piece within the limit, or 0.
    private static int LastSentenceEnd(string text, int limit)
    {
        for (int index = Math.Min(limit, text.Length) - 1; index > 0; index--)
        {
            char current = text[index];

            if (current is '。' or '！' or '？')
            {
                return index + 1;
            }

            if (current is '.' or '!' or '?' && index + 1 < text.Length && text[index + 1] == ' ')
            {
                return index + 1;
            }
        }

        return 0;
    }
}