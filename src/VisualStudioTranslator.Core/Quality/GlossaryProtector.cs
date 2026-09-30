using System.Text;
using System.Text.RegularExpressions;
using VisualStudioTranslator.Core.Documentation;

namespace VisualStudioTranslator.Core.Quality;

/// <summary>
/// Applies a <see cref="Glossary"/> to an already-<see cref="MarkupProtector"/>-protected
/// segment: every glossary term found in the segment's plain text becomes its own
/// placeholder, using the exact same token syntax and <see cref="ProtectedPlaceholder"/>
/// shape as markup protection, so a single <see cref="MarkupProtector.Restore"/> call
/// still restores everything - markup and glossary terms alike - in one pass. Kept as a
/// separate pass rather than folded into <see cref="MarkupProtector"/> itself, so each
/// can be tested and reasoned about independently.
/// </summary>
public static class GlossaryProtector
{
    public static ProtectedSegment Apply(ProtectedSegment segment, Glossary glossary)
    {
        if (glossary.Entries.Count == 0)
        {
            return segment;
        }

        Regex pattern = BuildPattern(glossary);
        Dictionary<string, GlossaryEntry> byTerm = BuildLookup(glossary);

        StringBuilder text = new();
        // Placeholders is IReadOnlyDictionary<int, ProtectedPlaceholder>, which does not
        // implement IDictionary<,> (rules out the by-dictionary constructor overload).
        // The parameterless ToDictionary() over IEnumerable<KeyValuePair<,>> is a recent
        // BCL addition not present on netstandard2.0 (Core's target) - the explicit
        // key/value selector overload below has existed since netstandard2.0 shipped and
        // works identically.
        Dictionary<int, ProtectedPlaceholder> placeholders = segment.Placeholders.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        int nextId = placeholders.Count == 0 ? 0 : placeholders.Keys.Max() + 1;

        // Only ever scan the plain-text spans, never a span that MarkupProtector
        // already turned into a placeholder token - a code span or cref must stay
        // exactly as it was, even if its own text happens to contain a glossary word.
        foreach ((string chunk, bool isPlaceholder) in MarkupProtector.SplitOnPlaceholders(segment.Text))
        {
            if (isPlaceholder)
            {
                text.Append(chunk);
                continue;
            }

            int position = 0;
            foreach (Match match in pattern.Matches(chunk))
            {
                text.Append(chunk, position, match.Index - position);
                position = match.Index + match.Length;

                GlossaryEntry entry = byTerm[match.Value];
                string replacementText = entry.Kind == GlossaryEntryKind.TranslateAs
                    ? (entry.Replacement is { Length: > 0 } r ? r : entry.Term)
                    : match.Value; // preserve the exact casing the author wrote

                int id = nextId++;
                placeholders[id] = new ProtectedPlaceholder
                {
                    Kind = PlaceholderKind.Node,
                    Node = new TextRun { Text = replacementText },
                };
                text.Append('⟦').Append(id).Append('⟧');
            }
            text.Append(chunk, position, chunk.Length - position);
        }

        return segment with { Text = text.ToString(), Placeholders = placeholders };
    }

    private static Dictionary<string, GlossaryEntry> BuildLookup(Glossary glossary)
    {
        // A loop, not a Select(...).ToDictionary() collection expression: later entries
        // for the same term (case-insensitively) must overwrite earlier ones via the
        // indexer, which ToDictionary's "duplicate key" behavior (throwing) does not
        // allow. Rejecting duplicate terms outright belongs to whatever later builds a
        // Glossary from user settings, not to this pure application step.
#pragma warning disable IDE0028
        Dictionary<string, GlossaryEntry> lookup = new(StringComparer.OrdinalIgnoreCase);
        foreach (GlossaryEntry entry in glossary.Entries)
        {
            lookup[entry.Term] = entry;
        }
        return lookup;
#pragma warning restore IDE0028
    }

    private static Regex BuildPattern(Glossary glossary)
    {
        // Longest term first: .NET's regex alternation tries each `|` branch in the
        // order written and takes the first that matches at a given position, rather
        // than the longest one - sorting longest-first is what makes "Task Scheduler"
        // win over "Task" when both are configured and both could match at that spot.
        IEnumerable<string> terms = glossary.Entries
            .Select(e => e.Term)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(t => t.Length);

        string alternation = string.Join("|", terms.Select(Regex.Escape));

        // Plain \b requires a transition between a word character and a non-word one,
        // which silently fails to bound a term that itself ends or starts with a
        // symbol: "C#" followed by whitespace sits between two non-word characters
        // (# and the space), so \b never fires there. Checking "not preceded/followed
        // by a letter or digit" directly sidesteps that and works uniformly for both
        // alphanumeric and symbol-bearing terms like "C#" or ".NET".
        return new Regex(
            $@"(?<![\p{{L}}\p{{N}}])(?:{alternation})(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}