using VisualStudioTranslator.Core.Caching;

namespace VisualStudioTranslator.Engine.Caching;

/// <summary>
/// Two caches used as one: a fast one in front, a slower one that survives restarts behind it.
/// A read tries the fast one first and, finding the answer only in the persistent one, brings it
/// forward so the next read is fast. A write goes to both. Neither level may throw, so nothing is
/// guarded here: if the persistent one is unavailable it simply never has an answer.
/// </summary>
internal sealed class TieredTranslationCache(ITranslationCache fast, ITranslationCache persistent)
    : ITranslationCache, IDisposable
{
    public async Task<CachedTranslation?> TryGetAsync(TranslationCacheKey key, CancellationToken cancellationToken)
    {
        CachedTranslation? hit = await fast.TryGetAsync(key, cancellationToken).ConfigureAwait(false);
        if (hit is not null)
        {
            return hit;
        }

        hit = await persistent.TryGetAsync(key, cancellationToken).ConfigureAwait(false);
        if (hit is not null)
        {
            await fast.SetAsync(key, hit, cancellationToken).ConfigureAwait(false);
        }

        return hit;
    }

    public async Task SetAsync(TranslationCacheKey key, CachedTranslation translation, CancellationToken cancellationToken)
    {
        await fast.SetAsync(key, translation, cancellationToken).ConfigureAwait(false);
        await persistent.SetAsync(key, translation, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        (fast as IDisposable)?.Dispose();
        (persistent as IDisposable)?.Dispose();
    }
}