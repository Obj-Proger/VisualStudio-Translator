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
    private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, string>>> _index = [];

    // Most recently used at the front, so the entry to evict is always the last one.
    private readonly LinkedList<KeyValuePair<string, string>> _recency = new();

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

    public Task<string?> TryGetAsync(TranslationCacheKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_index.TryGetValue(key.Value, out LinkedListNode<KeyValuePair<string, string>>? node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                return Task.FromResult<string?>(node.Value.Value);
            }
        }

        return Task.FromResult<string?>(null);
    }

    public Task SetAsync(TranslationCacheKey key, string translation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_index.TryGetValue(key.Value, out LinkedListNode<KeyValuePair<string, string>>? existing))
            {
                existing.Value = new KeyValuePair<string, string>(key.Value, translation);
                _recency.Remove(existing);
                _recency.AddFirst(existing);
            }
            else
            {
                _index[key.Value] = _recency.AddFirst(new KeyValuePair<string, string>(key.Value, translation));

                if (_index.Count > _capacity)
                {
                    LinkedListNode<KeyValuePair<string, string>> oldest = _recency.Last!;
                    _recency.RemoveLast();
                    _index.Remove(oldest.Value.Key);
                }
            }
        }

        return Task.CompletedTask;
    }
}