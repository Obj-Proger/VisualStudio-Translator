using System.Text;
using VisualStudioTranslator.Core.Documentation;

namespace VisualStudioTranslator.Core.Quality;

/// <summary>
/// What a translator is told besides a segment's protected text, so that it reads the segment as a person would:
/// the code each token stands for, and the paragraphs around it.
/// </summary>
public static class SegmentHints
{
    /// <summary>How much of each neighbouring paragraph goes into the context: enough for its sense, not its detail.</summary>
    public const int MaxNeighbourLength = 400;

    /// <summary>
    /// The code each stand-alone token of the segment stands for, as it is written in the documentation: a code
    /// span's text, a referenced name, a keyword. Tokens around translatable text (emphasis, a link's own words) are
    /// not code, and are left out.
    /// </summary>
    public static IReadOnlyDictionary<int, string> Codes(ProtectedSegment segment)
    {
        Dictionary<int, string> codes = [];

        foreach (KeyValuePair<int, ProtectedPlaceholder> placeholder in segment.Placeholders)
        {
            if (placeholder.Value.Kind == PlaceholderKind.Node
                && placeholder.Value.Node is { } node
                && TextOf(node) is { Length: > 0 } text)
            {
                codes[placeholder.Key] = text;
            }
        }

        return codes;
    }

    /// <summary>
    /// The paragraphs just before and just after segment <paramref name="index"/>, as plain text, or
    /// <see langword="null"/> when it has none. A long paragraph is cut, at a word where it can be.
    /// </summary>
    public static string? Around(IReadOnlyList<Segment> segments, int index)
    {
        List<string> neighbours = [];

        foreach (int other in new[] { index - 1, index + 1 })
        {
            if (other >= 0 && other < segments.Count && Shorten(TextOf(segments[other].Inlines)) is { Length: > 0 } text)
            {
                neighbours.Add(text);
            }
        }

        return neighbours.Count > 0 ? string.Join("\n\n", neighbours) : null;
    }

    /// <summary>The inlines as plain text, code as it is written.</summary>
    public static string TextOf(IReadOnlyList<Inline> inlines)
    {
        StringBuilder text = new();

        foreach (Inline inline in inlines)
        {
            text.Append(TextOf(inline));
        }

        return text.ToString();
    }

    private static string TextOf(Inline inline) => inline switch
    {
        TextRun run => run.Text,
        CodeSpan code => code.Text,
        EmphasisRun emphasis => TextOf(emphasis.Content),
        Ref { DisplayContent: { } display } => TextOf(display),
        Ref { Kind: RefKind.Cref } reference => NameOf(reference.Target),
        Ref reference => reference.Target,
        _ => string.Empty,
    };

    // "M:System.String.Join(System.String,System.String[])" is written as "Join", as a tooltip shows it.
    private static string NameOf(string cref)
    {
        string name = cref.Length > 2 && cref[1] == ':' ? cref.Substring(2) : cref;
        int parameters = name.IndexOf('(');
        if (parameters >= 0)
        {
            name = name.Substring(0, parameters);
        }

        int dot = name.LastIndexOf('.');
        return dot >= 0 && dot < name.Length - 1 ? name.Substring(dot + 1) : name;
    }

    private static string Shorten(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length <= MaxNeighbourLength)
        {
            return trimmed;
        }

        int space = trimmed.LastIndexOf(' ', MaxNeighbourLength);
        return trimmed.Substring(0, space > MaxNeighbourLength / 2 ? space : MaxNeighbourLength) + "…";
    }
}