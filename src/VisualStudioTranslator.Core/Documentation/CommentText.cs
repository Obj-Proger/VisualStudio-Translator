using System.Net;
using System.Text.RegularExpressions;

namespace VisualStudioTranslator.Core.Documentation;

/// <summary>
/// The prose in a piece of source the user selected to translate: comment markers and documentation tags taken
/// away, so the model sees only what a person would read. Text that is not a comment comes back as it was.
/// <list type="bullet">
/// <item>A selection is a comment when its first line begins one: <c>//</c>, <c>///</c>, <c>/*</c> or, in Visual
/// Basic, <c>'</c>. Then every line loses its marker, and the closing <c>*/</c>.</item>
/// <item>Documentation tags give way to what they stand for: a reference to its name, <c>&lt;c&gt;</c> to its
/// content. Sections such as <c>&lt;summary&gt;</c> and <c>&lt;para&gt;</c> become paragraphs, and a parameter's
/// description starts with its name.</item>
/// </list>
/// </summary>
public static class CommentText
{
    private static readonly Regex CommentStart = new(@"^\s*(//|/\*|')", RegexOptions.Compiled);
    private static readonly Regex LineMarker = new(@"^\s*(/{2,}|'{1,3}|/\*+|\*+(?!/))\s?", RegexOptions.Compiled);
    private static readonly Regex CommentEnd = new(@"\s*\*+/\s*$", RegexOptions.Compiled);

    private static readonly Regex Reference = new(
        @"<(?:see|seealso)\s+(?:cref|href|langword)\s*=\s*""(?:[A-Z]:)?([^""]*)""\s*/>", RegexOptions.Compiled);

    private static readonly Regex NameReference = new(
        @"<(?:paramref|typeparamref)\s+name\s*=\s*""([^""]*)""\s*/>", RegexOptions.Compiled);

    private static readonly Regex NamedSection = new(
        @"<(?:param|typeparam)\s+name\s*=\s*""([^""]*)""\s*>", RegexOptions.Compiled);

    private static readonly Regex SectionTag = new(
        @"</?(?:summary|remarks|returns|value|para|example|exception|param|typeparam|list|item)\b[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AnyTag = new(@"</?[A-Za-z][^>]*>", RegexOptions.Compiled);

    public static string Strip(string selection)
    {
        string text = (selection ?? string.Empty).Replace("\r\n", "\n");
        string firstLine = text.TrimStart('\n', ' ', '\t').Split('\n')[0];

        if (!CommentStart.IsMatch(firstLine))
        {
            return text;
        }

        string[] lines = text.Split('\n');

        for (int index = 0; index < lines.Length; index++)
        {
            lines[index] = CommentEnd.Replace(LineMarker.Replace(lines[index], string.Empty), string.Empty);
        }

        text = string.Join("\n", lines);
        text = Reference.Replace(text, "$1");
        text = NameReference.Replace(text, "$1");
        text = NamedSection.Replace(text, "\n\n$1: ");
        text = SectionTag.Replace(text, "\n\n");
        text = AnyTag.Replace(text, string.Empty);

        return WebUtility.HtmlDecode(text).Trim();
    }
}