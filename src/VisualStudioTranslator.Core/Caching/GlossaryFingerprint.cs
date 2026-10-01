using VisualStudioTranslator.Core.Quality;

namespace VisualStudioTranslator.Core.Caching;

/// <summary>
/// Reduces a <see cref="Glossary"/> to a short string for use in a cache key: the same
/// entries always give the same fingerprint, and any change to a term, its kind or its
/// replacement gives a different one. Without it, editing the glossary would keep serving
/// translations made under the old terms.
/// </summary>
public static class GlossaryFingerprint
{
    /// <summary>
    /// A <see langword="null"/> glossary and an empty one are the same thing and give the same
    /// fingerprint. Entry order is part of the fingerprint: if order never mattered,
    /// reordering would cost a few cache misses, but if it does matter and were ignored,
    /// the cache would serve wrong translations, so the safe direction is chosen.
    /// </summary>
    public static string Compute(Glossary? glossary)
    {
        IReadOnlyList<GlossaryEntry> entries = glossary?.Entries ?? [];

        return CanonicalHash.Compute(writer =>
        {
            writer.Write(entries.Count);

            foreach (GlossaryEntry entry in entries)
            {
                writer.Write(entry.Term);
                writer.Write((int)entry.Kind);

                // A null replacement and an empty one are different entries.
                writer.Write(entry.Replacement is not null);
                writer.Write(entry.Replacement ?? string.Empty);
            }
        });
    }
}