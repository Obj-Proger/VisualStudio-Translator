using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VisualStudioTranslator.Core.Caching;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Engine.Caching;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.Caching;

public sealed class SqliteTranslationCacheTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("vst-cache-");
    private readonly List<SqliteTranslationCache> _caches = [];

    private string DbPath => Path.Combine(_root.FullName, "data", "cache.db");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        // The connection holds the file open; it has to go before the folder can.
        foreach (SqliteTranslationCache cache in _caches)
        {
            cache.Dispose();
        }

        _root.Delete(recursive: true);
    }

    private SqliteTranslationCache Cache(int maxEntries = 1000, int pruneEvery = 100, TimeProvider? time = null, string? path = null)
    {
        SqliteTranslationCache cache = new(
            path ?? DbPath, NullLogger<SqliteTranslationCache>.Instance, maxEntries, pruneEvery, time);
        _caches.Add(cache);
        return cache;
    }

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
        (await Cache().TryGetAsync(Key("a"), Token)).Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ThenTryGetAsync_ReturnsTheEntryWithItsProvider()
    {
        SqliteTranslationCache cache = Cache();
        CachedTranslation entry = Entry("перевод ⟦0⟧", QualityTiers.Cloud, "azure", "v3");

        await cache.SetAsync(Key("a"), entry, Token);

        (await cache.TryGetAsync(Key("a"), Token)).Should().Be(entry);
    }

    [Fact]
    public async Task Entries_SurviveANewInstanceOverTheSameFile()
    {
        SqliteTranslationCache first = Cache();
        await first.SetAsync(Key("a"), Entry("сохранено"), Token);
        first.Dispose();

        (await Cache().TryGetAsync(Key("a"), Token))!.Text.Should().Be("сохранено");
    }

    [Fact]
    public async Task SetAsync_BetterTier_ReplacesTheWorseTranslation()
    {
        SqliteTranslationCache cache = Cache();

        await cache.SetAsync(Key("a"), Entry("local result"), Token);
        await cache.SetAsync(Key("a"), Entry("cloud result", QualityTiers.Cloud, "azure", "v3"), Token);

        CachedTranslation? stored = await cache.TryGetAsync(Key("a"), Token);
        stored!.Text.Should().Be("cloud result");
        stored.ProviderId.Should().Be("azure");
    }

    [Fact]
    public async Task SetAsync_WorseTier_NeverReplacesABetterTranslation()
    {
        SqliteTranslationCache cache = Cache();

        await cache.SetAsync(Key("a"), Entry("cloud result", QualityTiers.Cloud, "azure", "v3"), Token);
        await cache.SetAsync(Key("a"), Entry("late local result"), Token);

        (await cache.TryGetAsync(Key("a"), Token))!.Text.Should().Be("cloud result");
    }

    [Fact]
    public async Task SetAsync_SameTier_Replaces()
    {
        SqliteTranslationCache cache = Cache();

        await cache.SetAsync(Key("a"), Entry("old model", revision: "1"), Token);
        await cache.SetAsync(Key("a"), Entry("new model", revision: "2"), Token);

        (await cache.TryGetAsync(Key("a"), Token))!.Text.Should().Be("new model");
        cache.Count.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentWritesToOneKey_EndWithTheBestTierWhateverTheOrder()
    {
        SqliteTranslationCache cache = Cache();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, 300),
            Token,
            async (i, ct) =>
            {
                CachedTranslation entry = i == 150
                    ? Entry("cloud result", QualityTiers.Cloud, "azure", "v3")
                    : Entry($"local {i}");
                await cache.SetAsync(Key("k"), entry, ct);
            });

        (await cache.TryGetAsync(Key("k"), Token))!.Text.Should().Be("cloud result");
    }

    [Fact]
    public async Task SetAsync_BeyondTheLimit_RemovesTheLeastRecentlyUsedEntries()
    {
        ManualTime time = new(DateTimeOffset.UnixEpoch.AddDays(1));
        SqliteTranslationCache cache = Cache(maxEntries: 5, pruneEvery: 1, time: time);

        for (int i = 0; i < 20; i++)
        {
            time.Now = time.Now.AddMinutes(1);
            await cache.SetAsync(Key($"k{i}"), Entry($"v{i}"), Token);
        }

        cache.Count.Should().Be(5);
        (await cache.TryGetAsync(Key("k19"), Token)).Should().NotBeNull();
        (await cache.TryGetAsync(Key("k0"), Token)).Should().BeNull();
    }

    [Fact]
    public async Task TryGetAsync_MarksAnEntryAsRecentlyUsed_SoItSurvivesPruning()
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch.AddDays(10);
        ManualTime time = new(start);
        SqliteTranslationCache cache = Cache(maxEntries: 3, pruneEvery: 1, time: time);

        await cache.SetAsync(Key("a"), Entry("A"), Token);
        time.Now = start.AddHours(10);
        await cache.SetAsync(Key("b"), Entry("B"), Token);
        time.Now = start.AddHours(20);
        await cache.SetAsync(Key("c"), Entry("C"), Token);

        // "a" is the oldest, until it is read.
        time.Now = start.AddHours(30);
        await cache.TryGetAsync(Key("a"), Token);

        time.Now = start.AddHours(40);
        await cache.SetAsync(Key("d"), Entry("D"), Token);

        (await cache.TryGetAsync(Key("a"), Token)).Should().NotBeNull();
        (await cache.TryGetAsync(Key("b"), Token)).Should().BeNull();
        (await cache.TryGetAsync(Key("c"), Token)).Should().NotBeNull();
        (await cache.TryGetAsync(Key("d"), Token)).Should().NotBeNull();
    }

    [Fact]
    public async Task DamagedFile_IsReplacedAndTheCacheKeepsWorking()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        // Not zeros: an all-zero file is a valid empty database. This is plainly not one.
        File.WriteAllBytes(DbPath, Enumerable.Repeat((byte)'A', 4096).ToArray());
        SqliteTranslationCache cache = Cache();

        await cache.SetAsync(Key("a"), Entry("after recovery"), Token);

        (await cache.TryGetAsync(Key("a"), Token))!.Text.Should().Be("after recovery");
    }

    [Fact]
    public async Task FileFromAnotherSchemaVersion_IsStartedOver()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        using (SqliteConnection old = new($"Data Source={DbPath};Pooling=False"))
        {
            old.Open();
            using SqliteCommand command = old.CreateCommand();
            command.CommandText = "CREATE TABLE translations (something TEXT); PRAGMA user_version = 99;";
            command.ExecuteNonQuery();
        }

        SqliteTranslationCache cache = Cache();
        await cache.SetAsync(Key("a"), Entry("fresh"), Token);

        (await cache.TryGetAsync(Key("a"), Token))!.Text.Should().Be("fresh");
    }

    [Fact]
    public async Task UnusableLocation_NeverThrowsAndSimplyHasNothing()
    {
        // A file where the folder should be: the database can never be created there.
        string blocker = Path.Combine(_root.FullName, "blocker");
        File.WriteAllText(blocker, "not a folder");
        SqliteTranslationCache cache = Cache(path: Path.Combine(blocker, "cache.db"));

        for (int i = 0; i < 8; i++)
        {
            await cache.SetAsync(Key($"k{i}"), Entry("x"), Token);
            (await cache.TryGetAsync(Key($"k{i}"), Token)).Should().BeNull();
        }

        cache.Count.Should().Be(0);
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceled()
    {
        SqliteTranslationCache cache = Cache();
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> act = () => cache.TryGetAsync(Key("a"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}