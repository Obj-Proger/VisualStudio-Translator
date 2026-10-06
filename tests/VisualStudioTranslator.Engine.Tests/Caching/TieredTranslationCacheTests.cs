using AwesomeAssertions;
using VisualStudioTranslator.Core.Caching;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Engine.Caching;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.Caching;

public sealed class TieredTranslationCacheTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static TranslationCacheKey Key(string value) => new() { Value = value };

    private static CachedTranslation Entry(string text) => new()
    {
        Text = text,
        ProviderId = "local",
        ProviderRevision = "1",
        QualityTier = QualityTiers.Compact,
    };

    [Fact]
    public async Task TryGetAsync_NeitherLevelHasIt_ReturnsNull()
    {
        TieredTranslationCache cache = new(new MemoryTranslationCache(), new MemoryTranslationCache());

        (await cache.TryGetAsync(Key("a"), Token)).Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_WritesToBothLevels()
    {
        MemoryTranslationCache fast = new();
        MemoryTranslationCache persistent = new();
        TieredTranslationCache cache = new(fast, persistent);

        await cache.SetAsync(Key("a"), Entry("x"), Token);

        (await fast.TryGetAsync(Key("a"), Token)).Should().NotBeNull();
        (await persistent.TryGetAsync(Key("a"), Token)).Should().NotBeNull();
    }

    [Fact]
    public async Task TryGetAsync_FoundOnlyInThePersistentLevel_IsBroughtForward()
    {
        MemoryTranslationCache fast = new();
        MemoryTranslationCache persistent = new();
        await persistent.SetAsync(Key("a"), Entry("from disk"), Token);
        TieredTranslationCache cache = new(fast, persistent);

        CachedTranslation? hit = await cache.TryGetAsync(Key("a"), Token);

        hit!.Text.Should().Be("from disk");
        (await fast.TryGetAsync(Key("a"), Token))!.Text.Should().Be("from disk");
    }

    [Fact]
    public async Task TryGetAsync_FoundInTheFastLevel_DoesNotAskThePersistentOne()
    {
        MemoryTranslationCache fast = new();
        CountingCache persistent = new();
        await fast.SetAsync(Key("a"), Entry("from memory"), Token);
        TieredTranslationCache cache = new(fast, persistent);

        (await cache.TryGetAsync(Key("a"), Token))!.Text.Should().Be("from memory");

        persistent.Reads.Should().Be(0);
    }

    [Fact]
    public void Dispose_DisposesTheLevelsThatNeedIt()
    {
        CountingCache persistent = new();
        TieredTranslationCache cache = new(new MemoryTranslationCache(), persistent);

        cache.Dispose();

        persistent.Disposed.Should().BeTrue();
    }

    private sealed class CountingCache : ITranslationCache, IDisposable
    {
        public int Reads { get; private set; }

        public bool Disposed { get; private set; }

        public Task<CachedTranslation?> TryGetAsync(TranslationCacheKey key, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<CachedTranslation?>(null);
        }

        public Task SetAsync(TranslationCacheKey key, CachedTranslation translation, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Dispose() => Disposed = true;
    }
}