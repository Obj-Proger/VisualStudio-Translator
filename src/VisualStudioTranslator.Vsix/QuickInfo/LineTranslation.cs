using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Vsix.Translation;

namespace VisualStudioTranslator.Vsix.QuickInfo;

/// <summary>
/// Translates lines read from a popup, a tooltip's or signature help's: the lines go to the Engine as one documentation
/// comment, a paragraph each, with the code kept out of the translation; the same lines translated before come from
/// the Engine's cache. What comes back is the lines whose translation differs from the original, each with the caption
/// it stood under; text already in the language asked for comes back unchanged and is left out.
/// </summary>
internal static class LineTranslation
{
    /// <param name="lines">Every line of the popup, in order: the captions are found among them.</param>
    /// <param name="prose">The lines to translate, taken from <paramref name="lines"/>.</param>
    public static async Task<IReadOnlyList<TranslatedLine>> TranslateAsync(
        IReadOnlyList<TooltipLine> lines, IReadOnlyList<TooltipLine> prose)
    {
        IReadOnlyList<IReadOnlyList<TooltipRun>> runs = [.. prose.Select(line => line.Runs)];
        string documentation = DocumentationXmlWriter.Write(TooltipText.ToDocument(runs));

        DocumentModel? translated = await DocumentationTranslator.Shared.TranslateAsync(documentation).ConfigureAwait(false);

        return translated is null ? [] : Pair(lines, prose, TooltipText.FromDocument(runs, translated));
    }

    // The translated lines that differ from their originals, each with the caption it stood under.
    private static List<TranslatedLine> Pair(
        IReadOnlyList<TooltipLine> lines, IReadOnlyList<TooltipLine> prose, IReadOnlyList<IReadOnlyList<TranslatedPiece>?> translated)
    {
        List<TranslatedLine> result = [];

        for (int index = 0; index < prose.Count; index++)
        {
            if (translated[index] is not { } pieces)
            {
                continue;
            }

            TranslatedLine line = new(prose[index], pieces, CaptionOf(lines, prose[index]));

            if (!string.Equals(Normalize(line.Text), Normalize(line.Original.Text), StringComparison.Ordinal))
            {
                result.Add(line);
            }
        }

        return result;
    }

    // The line above, blank lines aside, when it is a caption such as "Returns:" rather than prose.
    private static TooltipLine? CaptionOf(IReadOnlyList<TooltipLine> lines, TooltipLine line)
    {
        int index = IndexOf(lines, line) - 1;
        while (index >= 0 && string.IsNullOrWhiteSpace(lines[index].Text))
        {
            index--;
        }

        TooltipLine? above = index >= 0 ? lines[index] : null;

        return above is not null && !TooltipText.IsProse(above.Runs) && above.Text.TrimEnd().EndsWith(":", StringComparison.Ordinal)
            ? above
            : null;
    }

    private static int IndexOf(IReadOnlyList<TooltipLine> lines, TooltipLine line)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            if (ReferenceEquals(lines[index], line))
            {
                return index;
            }
        }

        return -1;
    }

    private static string Normalize(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}