namespace VisualStudioTranslator.Core.Caching;

/// <summary>
/// A store of finished translations. It is an optimization and never a dependency: if the
/// store is unavailable or corrupt, the translation is simply made again.
/// <list type="bullet">
/// <item>What is stored is the provider's output for a protected segment, placeholder tokens
/// still in it, not rebuilt inline content, together with who made it
/// (<see cref="CachedTranslation"/>). It is restored against the current segment's
/// placeholders after being read.</item>
/// <item>One key holds one translation, and a better one replaces a worse one:
/// <see cref="SetAsync"/> must apply <see cref="CachedTranslation.ShouldReplace"/> atomically,
/// so a translation is never replaced by one of a lower tier, however the writes interleave.
/// Whether a stored translation may be <em>served</em> is the caller's decision, through
/// <see cref="CachedTranslation.Satisfies"/>; the cache just returns what it has.</item>
/// <item>Only results that passed <see cref="Quality.TranslationValidator"/> belong here.
/// That is the caller's responsibility; the cache cannot tell a good result from a bad one.</item>
/// <item>Implementations never throw because of storage trouble: a read that fails is a miss
/// and a write that fails is dropped. Only <see cref="OperationCanceledException"/> may
/// escape.</item>
/// <item>The source text is never stored, only the key and the translation.</item>
/// <item>May be called from several threads at once.</item>
/// </list>
/// </summary>
public interface ITranslationCache
{
    /// <returns>The stored translation, or <see langword="null"/> on a miss.</returns>
    Task<CachedTranslation?> TryGetAsync(TranslationCacheKey key, CancellationToken cancellationToken);

    Task SetAsync(TranslationCacheKey key, CachedTranslation translation, CancellationToken cancellationToken);
}