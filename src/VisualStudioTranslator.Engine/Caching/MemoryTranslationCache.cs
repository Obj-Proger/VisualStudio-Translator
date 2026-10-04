using VisualStudioTranslator.Core.Caching;

namespace VisualStudioTranslator.Engine.Caching;

/// <summary>
/// The first cache level: a bounded in-process store that evicts the entry used longest ago.
/// The bound matters because the Engine is a long-lived process and every document the user
/// hovers adds entries; without it the cache would only ever grow.
/// </summary>
internal sealed class MemoryTranslationCache : ITranslationCache
{
    public const int DefaultCapacity = 10_000;

    private readonly int _capacity;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, CachedTranslation>>> _index = [];

    // Most recently used at the front, so the entry to evict is always the last one.
    private readonly LinkedList<KeyValuePair<string, CachedTranslation>> _recency = new();

    public MemoryTranslationCache(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _index.Count;
            }
        }
    }

    public Task<CachedTranslation?> TryGetAsync(TranslationCacheKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_index.TryGetValue(key.Value, out LinkedListNode<KeyValuePair<string, CachedTranslation>>? node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                return Task.FromResult<CachedTranslation?>(node.Value.Value);
            }
        }

        return Task.FromResult<CachedTranslation?>(null);
    }

    public Task SetAsync(TranslationCacheKey key, CachedTranslation translation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_index.TryGetValue(key.Value, out LinkedListNode<KeyValuePair<string, CachedTranslation>>? existing))
            {
                // Decided inside the lock, together with the write: a slow local translation that
                // finishes after a cloud one must not be able to undo it.
                if (CachedTranslation.ShouldReplace(existing.Value.Value, translation))
                {
                    existing.Value = new KeyValuePair<string, CachedTranslation>(key.Value, translation);
                }

                _recency.Remove(existing);
                _recency.AddFirst(existing);
            }
            else
            {
                _index[key.Value] = _recency.AddFirst(new KeyValuePair<string, CachedTranslation>(key.Value, translation));

                if (_index.Count > _capacity)
                {
                    LinkedListNode<KeyValuePair<string, CachedTranslation>> oldest = _recency.Last!;
                    _recency.RemoveLast();
                    _index.Remove(oldest.Value.Key);
                }
            }
        }

        return Task.CompletedTask;
    }
}