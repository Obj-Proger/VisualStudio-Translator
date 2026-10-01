namespace VisualStudioTranslator.Core.Caching;

/// <summary>
/// A store of finished translations. It is an optimization and never a dependency: if the
/// store is unavailable or corrupt, the translation is simply made again.
/// <list type="bullet">
/// <item>What is stored is the provider's output for a protected segment, placeholder tokens
/// still in it, not rebuilt inline content. It is restored against the current segment's
/// placeholders after being read.</item>
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
    /// <returns>The cached translation, or <see langword="null"/> on a miss.</returns>
    Task<string?> TryGetAsync(TranslationCacheKey key, CancellationToken cancellationToken);

    Task SetAsync(TranslationCacheKey key, string translation, CancellationToken cancellationToken);
}