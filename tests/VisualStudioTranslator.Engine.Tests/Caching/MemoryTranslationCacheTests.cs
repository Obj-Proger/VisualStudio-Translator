using AwesomeAssertions;
using VisualStudioTranslator.Core.Caching;
using VisualStudioTranslator.Engine.Caching;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.Caching;

public sealed class MemoryTranslationCacheTests
{
    private static TranslationCacheKey Key(string value) => new() { Value = value };

    [Fact]
    public async Task TryGetAsync_UnknownKey_ReturnsNull()
    {
        MemoryTranslationCache cache = new();

        (await cache.TryGetAsync(Key("a"), TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ThenTryGetAsync_ReturnsTheStoredTranslation()
    {
        MemoryTranslationCache cache = new();

        await cache.SetAsync(Key("a"), "перевод", TestContext.Current.CancellationToken);

        (await cache.TryGetAsync(Key("a"), TestContext.Current.CancellationToken)).Should().Be("перевод");
    }

    [Fact]
    public async Task SetAsync_ExistingKey_OverwritesWithoutGrowing()
    {
        MemoryTranslationCache cache = new();

        await cache.SetAsync(Key("a"), "first", TestContext.Current.CancellationToken);
        await cache.SetAsync(Key("a"), "second", TestContext.Current.CancellationToken);

        (await cache.TryGetAsync(Key("a"), TestContext.Current.CancellationToken)).Should().Be("second");
        cache.Count.Should().Be(1);
    }

    [Fact]
    public async Task SetAsync_BeyondCapacity_EvictsTheLeastRecentlyUsedEntry()
    {
        MemoryTranslationCache cache = new(capacity: 2);
        CancellationToken token = TestContext.Current.CancellationToken;

        await cache.SetAsync(Key("a"), "A", token);
        await cache.SetAsync(Key("b"), "B", token);
        await cache.TryGetAsync(Key("a"), token); // "a" is now more recent than "b"
        await cache.SetAsync(Key("c"), "C", token);

        (await cache.TryGetAsync(Key("b"), token)).Should().BeNull();
        (await cache.TryGetAsync(Key("a"), token)).Should().Be("A");
        (await cache.TryGetAsync(Key("c"), token)).Should().Be("C");
        cache.Count.Should().Be(2);
    }

    [Fact]
    public void Constructor_NonPositiveCapacity_Throws()
    {
        Action act = () => _ = new MemoryTranslationCache(capacity: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ConcurrentWrites_NeverExceedCapacity()
    {
        MemoryTranslationCache cache = new(capacity: 100);
        CancellationToken token = TestContext.Current.CancellationToken;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, 2000),
            token,
            async (i, ct) => await cache.SetAsync(Key($"key-{i}"), $"value-{i}", ct));

        cache.Count.Should().Be(100);
    }

    [Fact]
    public async Task TryGetAsync_CancelledToken_ThrowsOperationCanceled()
    {
        MemoryTranslationCache cache = new();
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> act = () => cache.TryGetAsync(Key("a"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}