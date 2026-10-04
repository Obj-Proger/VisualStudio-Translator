namespace VisualStudioTranslator.Core.Caching;

/// <summary>
/// Versions that are folded into every <see cref="TranslationCacheKey"/>. Raising either
/// one retires all existing cache entries at once, which is the point: an entry that could
/// be wrong is simply never found again, and ages out.
/// </summary>
public static class CacheVersions
{
    /// <summary>
    /// How cache keys are derived and what is stored under them. Raise it whenever
    /// <see cref="TranslationCacheKey.Create"/> or the stored value's meaning changes. A
    /// test pins the derivation, so forgetting to raise this fails loudly.
    /// <para>
    /// 2: the provider left the key and moved into the entry (<see cref="CachedTranslation"/>),
    /// so a better provider's translation can replace a worse one.
    /// </para>
    /// </summary>
    public const int Schema = 2;

    /// <summary>
    /// The behavior of the translation pipeline around the provider. Raise it whenever a
    /// change could make a previously cached result wrong even though the text sent to the
    /// provider is unchanged: how placeholders are restored, how the glossary is applied,
    /// what the validator accepts. Changes that alter the text itself (segmentation, the
    /// placeholder syntax) already change the key and do not strictly need it, but raising
    /// it then does no harm. Nothing enforces this; it is a review checklist item.
    /// </summary>
    public const int Pipeline = 1;
}