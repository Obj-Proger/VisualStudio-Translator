using AwesomeAssertions;
using VisualStudioTranslator.Core.Caching;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Engine.Caching;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.Caching;

public sealed class MemoryTranslationCacheTests
{
    private static TranslationCacheKey Key(string value) => new() { Value = value };

    private static CachedTranslation Entry(
        string text, int tier = QualityTiers.Compact, string providerId = "local", string revision = "1") => new()
        {
            Text = text,
            ProviderId = providerId,
            ProviderRevision = revision,
            QualityTier = tier,
        };

    [Fact]
    public async Task TryGetAsync_UnknownKey_ReturnsNull()
    {
        MemoryTranslationCache cache = new();

        (await cache.TryGetAsync(Key("a"), TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ThenTryGetAsync_ReturnsTheStoredEntryWithItsProvider()
    {
        MemoryTranslationCache cache = new();
        CachedTranslation entry = Entry("перевод", QualityTiers.Cloud, "azure", "v3");

        await cache.SetAsync(Key("a"), entry, TestContext.Current.CancellationToken);

        (await cache.TryGetAsync(Key("a"), TestContext.Current.CancellationToken)).Should().Be(entry);
    }

    [Fact]
    public async Task SetAsync_SameTier_ReplacesWithoutGrowing()
    {
        MemoryTranslationCache cache = new();
        CancellationToken token = TestContext.Current.CancellationToken;

        await cache.SetAsync(Key("a"), Entry("first", revision: "1"), token);
        await cache.SetAsync(Key("a"), Entry("second", revision: "2"), token);

        (await cache.TryGetAsync(Key("a"), token))!.Text.Should().Be("second");
        cache.Count.Should().Be(1);
    }

    [Fact]
    public async Task SetAsync_BetterTier_ReplacesTheWorseTranslation()
    {
        MemoryTranslationCache cache = new();
        CancellationToken token = TestContext.Current.CancellationToken;

        await cache.SetAsync(Key("a"), Entry("local result"), token);
        await cache.SetAsync(Key("a"), Entry("cloud result", QualityTiers.Cloud, "azure", "v3"), token);

        CachedTranslation? stored = await cache.TryGetAsync(Key("a"), token);

        stored!.Text.Should().Be("cloud result");
        stored.ProviderId.Should().Be("azure");
    }

    [Fact]
    public async Task SetAsync_WorseTier_NeverReplacesABetterTranslation()
    {
        MemoryTranslationCache cache = new();
        CancellationToken token = TestContext.Current.CancellationToken;

        await cache.SetAsync(Key("a"), Entry("cloud result", QualityTiers.Cloud, "azure", "v3"), token);
        await cache.SetAsync(Key("a"), Entry("late local result"), token);

        (await cache.TryGetAsync(Key("a"), token))!.Text.Should().Be("cloud result");
    }

    [Fact]
    public async Task SetAsync_BeyondCapacity_EvictsTheLeastRecentlyUsedEntry()
    {
        MemoryTranslationCache cache = new(capacity: 2);
        CancellationToken token = TestContext.Current.CancellationToken;

        await cache.SetAsync(Key("a"), Entry("A"), token);
        await cache.SetAsync(Key("b"), Entry("B"), token);
        await cache.TryGetAsync(Key("a"), token); // "a" is now more recent than "b"
        await cache.SetAsync(Key("c"), Entry("C"), token);

        (await cache.TryGetAsync(Key("b"), token)).Should().BeNull();
        (await cache.TryGetAsync(Key("a"), token))!.Text.Should().Be("A");
        (await cache.TryGetAsync(Key("c"), token))!.Text.Should().Be("C");
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
            async (i, ct) => await cache.SetAsync(Key($"key-{i}"), Entry($"value-{i}"), ct));

        cache.Count.Should().Be(100);
    }

    [Fact]
    public async Task ConcurrentWritesToOneKey_EndWithTheBestTierWhateverTheOrder()
    {
        MemoryTranslationCache cache = new();
        CancellationToken token = TestContext.Current.CancellationToken;

        // A burst of local writes racing with one cloud write: the cloud translation must survive.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, 500),
            token,
            async (i, ct) =>
            {
                CachedTranslation entry = i == 250
                    ? Entry("cloud result", QualityTiers.Cloud, "azure", "v3")
                    : Entry($"local {i}");
                await cache.SetAsync(Key("k"), entry, ct);
            });

        (await cache.TryGetAsync(Key("k"), token))!.Text.Should().Be("cloud result");
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