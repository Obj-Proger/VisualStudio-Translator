using VisualStudioTranslator.Core.Providers.Abstractions;

namespace VisualStudioTranslator.Core.Caching;

/// <summary>
/// A translation as the cache stores it: the text, and who made it. Knowing the maker is what
/// lets a better translation replace a worse one without ever going the other way. The
/// source text is not part of it, only what the provider returned.
/// </summary>
public sealed record CachedTranslation
{
    /// <summary>The provider's output for a protected segment, placeholder tokens still in it.</summary>
    public required string Text { get; init; }

    public required string ProviderId { get; init; }

    public required string ProviderRevision { get; init; }

    public required int QualityTier { get; init; }

    public static CachedTranslation From(ProviderInfo provider, string text) => new()
    {
        Text = text,
        ProviderId = provider.Id,
        ProviderRevision = provider.Revision,
        QualityTier = provider.QualityTier,
    };

    /// <summary>
    /// Whether this entry may be served to a request that <paramref name="provider"/> would
    /// otherwise answer. A translation of a better tier always may: it is what the user would
    /// rather read, and it stays valid when the better provider is switched off. One of the same
    /// tier only if the same provider made it under the same revision, so the user's choice of
    /// provider is respected and an outdated model's output is not reused. One of a worse
    /// tier never may: that is the whole point of asking a better provider.
    /// </summary>
    public bool Satisfies(ProviderInfo provider)
    {
        if (QualityTier > provider.QualityTier)
        {
            return true;
        }

        return QualityTier == provider.QualityTier
            && string.Equals(ProviderId, provider.Id, StringComparison.Ordinal)
            && string.Equals(ProviderRevision, provider.Revision, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether <paramref name="incoming"/> should take the place of <paramref name="existing"/>.
    /// Anything may fill an empty slot; after that only an equal or better tier may replace, so a
    /// slow local translation that finishes after a cloud one cannot undo it. A cache must
    /// apply this atomically with the write, not as a read followed by a write.
    /// </summary>
    public static bool ShouldReplace(CachedTranslation? existing, CachedTranslation incoming) =>
        existing is null || incoming.QualityTier >= existing.QualityTier;
}