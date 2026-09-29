using System.Text;
using System.Text.RegularExpressions;
using VisualStudioTranslator.Core.Documentation;

namespace VisualStudioTranslator.Core.Quality;

/// <summary>
/// Implements the "no markup support" protection strategy (<c>ProviderCapabilities</c>'s
/// <c>MarkupSupport.None</c> - the universal fallback every provider must support, since
/// it needs nothing from the wire format). <c>CodeSpan</c> and a self-closing
/// <c>Ref</c> become a single opaque placeholder token; <c>EmphasisRun</c> and a
/// <c>Ref</c> carrying its own display text become a start/end marker pair so the
/// translatable content inside them still gets translated. HTML- and XML-tag-based
/// strategies for providers that do understand markup are a separate, later
/// implementation behind the same shape once real provider capabilities exist to choose
/// between them.
/// </summary>
public static class MarkupProtector
{
    // ⟦N⟧ for a whole node; ⟦N:o⟧ / ⟦N:c⟧ for a wrapper's open/close marker.
    private static readonly Regex PlaceholderPattern = new(@"⟦(\d+)(:[oc])?⟧", RegexOptions.Compiled);

    public static ProtectedSegment Protect(IReadOnlyList<Inline> inlines)
    {
        StringBuilder text = new();
        Dictionary<int, ProtectedPlaceholder> placeholders = [];
        int nextId = 0;

        AppendInlines(inlines, text, placeholders, ref nextId);

        return new ProtectedSegment { Text = text.ToString(), Placeholders = placeholders };
    }

    /// <summary>
    /// Rebuilds inline content from <paramref name="translatedText"/> - the translated
    /// counterpart of a <see cref="ProtectedSegment.Text"/> - using
    /// <paramref name="placeholders"/> to interpret the placeholder tokens it still
    /// contains. Never throws: a placeholder token that is missing, out of order, or
    /// otherwise does not match what <see cref="Protect"/> produced is dropped or
    /// closed with whatever content was collected rather than raising an exception.
    /// Deciding whether that degradation is acceptable for a given segment - retrying,
    /// falling back to another provider, or showing the original text - is a separate,
    /// later concern; this method's job is only to never crash the pipeline.
    /// </summary>
    public static IReadOnlyList<Inline> Restore(string translatedText, IReadOnlyDictionary<int, ProtectedPlaceholder> placeholders)
    {
        List<Inline> result = [];
        Stack<(int Id, List<Inline> Buffer)> openWrappers = new();
        StringBuilder pendingText = new();

        List<Inline> CurrentBuffer() => openWrappers.Count > 0 ? openWrappers.Peek().Buffer : result;

        void FlushPendingText()
        {
            if (pendingText.Length > 0)
            {
                CurrentBuffer().Add(new TextRun { Text = pendingText.ToString() });
                pendingText.Clear();
            }
        }

        int position = 0;
        foreach (Match match in PlaceholderPattern.Matches(translatedText))
        {
            pendingText.Append(translatedText, position, match.Index - position);
            position = match.Index + match.Length;

            int id = int.Parse(match.Groups[1].Value);
            string? marker = match.Groups[2].Success ? match.Groups[2].Value : null;

            if (marker is null)
            {
                FlushPendingText();
                if (placeholders.TryGetValue(id, out ProtectedPlaceholder? placeholder) && placeholder.Node is not null)
                {
                    CurrentBuffer().Add(placeholder.Node);
                }
                // An id with no matching whole-node entry is dropped rather than
                // guessed at.
            }
            else if (marker == ":o")
            {
                FlushPendingText();
                openWrappers.Push((id, []));
            }
            else // ":c"
            {
                FlushPendingText();
                if (openWrappers.Count > 0 && openWrappers.Peek().Id == id)
                {
                    (_, List<Inline> content) = openWrappers.Pop();
                    CurrentBuffer().Add(BuildWrapper(id, content, placeholders));
                }
                // A close marker that does not match the innermost open wrapper (or
                // none is open at all) is dropped rather than guessed at.
            }
        }

        pendingText.Append(translatedText, position, translatedText.Length - position);
        FlushPendingText();

        // Any wrapper still open when the text ends - its close marker was lost -
        // is closed here using whatever content it accumulated, rather than
        // discarding that content outright.
        while (openWrappers.Count > 0)
        {
            (int id, List<Inline> content) = openWrappers.Pop();
            CurrentBuffer().Add(BuildWrapper(id, content, placeholders));
        }

        return result;
    }

    private static void AppendInlines(
        IReadOnlyList<Inline> inlines, StringBuilder text, Dictionary<int, ProtectedPlaceholder> placeholders, ref int nextId)
    {
        foreach (Inline inline in inlines)
        {
            switch (inline)
            {
                case TextRun run:
                    text.Append(run.Text);
                    break;

                case CodeSpan or Ref { DisplayContent: null }:
                    int nodeId = nextId++;
                    placeholders[nodeId] = new ProtectedPlaceholder { Kind = PlaceholderKind.Node, Node = inline };
                    text.Append('⟦').Append(nodeId).Append('⟧');
                    break;

                case EmphasisRun emphasis:
                    int emphasisId = nextId++;
                    placeholders[emphasisId] = new ProtectedPlaceholder { Kind = PlaceholderKind.EmphasisWrapper };
                    text.Append('⟦').Append(emphasisId).Append(":o⟧");
                    AppendInlines(emphasis.Content, text, placeholders, ref nextId);
                    text.Append('⟦').Append(emphasisId).Append(":c⟧");
                    break;

                case Ref { DisplayContent: { } display } reference:
                    int refId = nextId++;
                    placeholders[refId] = new ProtectedPlaceholder
                    {
                        Kind = PlaceholderKind.RefWrapper,
                        RefKind = reference.Kind,
                        RefTarget = reference.Target,
                    };
                    text.Append('⟦').Append(refId).Append(":o⟧");
                    AppendInlines(display, text, placeholders, ref nextId);
                    text.Append('⟦').Append(refId).Append(":c⟧");
                    break;
            }
        }
    }

    private static Inline BuildWrapper(int id, List<Inline> content, IReadOnlyDictionary<int, ProtectedPlaceholder> placeholders)
    {
        if (placeholders.TryGetValue(id, out ProtectedPlaceholder? placeholder)
            && placeholder.Kind == PlaceholderKind.RefWrapper
            && placeholder.RefKind is { } refKind)
        {
            return new Ref
            {
                Kind = refKind,
                Target = placeholder.RefTarget ?? string.Empty,
                DisplayContent = content.Count > 0 ? content : null,
            };
        }

        // Default for PlaceholderKind.EmphasisWrapper and for any id the map does not
        // recognize - re-wrapping as emphasis is a safe, visible-but-harmless choice
        // rather than losing the collected content.
        return new EmphasisRun { Content = content };
    }
}