using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Caching;

namespace VisualStudioTranslator.Engine.Caching;

/// <summary>
/// The second cache level: translations kept on disk, so they survive the Engine restarting.
/// Without it every restart would discard all the work done so far, and a translation paid for
/// at a cloud provider would be paid for again.
/// <list type="bullet">
/// <item>It is disposable by design. A file that is damaged, or written by another version of the
/// schema, is simply deleted and started over, and a file that cannot be used at all turns this
/// level off for the rest of the process. The cache is an optimization; whatever goes wrong here
/// costs speed, never a translation (principle P7).</item>
/// <item>"A better translation is never replaced by a worse one" is enforced by the database
/// itself, in the one statement that writes, so no interleaving of writes can break it.</item>
/// <item>Everything runs on one connection under one lock. The operations take well under a
/// millisecond and this process is the only one writing, so there is nothing to gain from a pool
/// and no "database is locked" to handle.</item>
/// <item>The source text is never stored: only the hashed key, the translated text and who
/// made it. The translations themselves do sit here in plain text, in the user's profile.</item>
/// </list>
/// </summary>
internal sealed class SqliteTranslationCache(
    string path,
    ILogger<SqliteTranslationCache> logger,
    int maxEntries = SqliteTranslationCache.DefaultMaxEntries,
    int pruneEvery = SqliteTranslationCache.DefaultPruneEvery,
    TimeProvider? time = null) : ITranslationCache, IDisposable
{
    public const int DefaultMaxEntries = 100_000;
    public const int DefaultPruneEvery = 100;

    private const int SchemaVersion = 1;
    private const int MaxConsecutiveFailures = 5;
    private const int MaxRecoveries = 2;

    // Reading an entry marks it as recently used, but at most once an hour: otherwise every read
    // would be a write, for the sake of an ordering that does not need to be that exact.
    private const long TouchThresholdSeconds = 3600;

    // SQLite result codes for "this file is not a usable database".
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = false,
    }.ToString();

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private SqliteConnection? _connection;
    private bool _disabled;
    private int _failures;
    private int _recoveries;
    private int _writesSincePrune;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualStudioTranslator",
        "cache.db");

    /// <summary>How many entries are stored right now. For tests; 0 when the cache is unavailable.</summary>
    internal int Count =>
        Run("count", connection => (int)Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM translations;")), fallback: 0);

    public Task<CachedTranslation?> TryGetAsync(TranslationCacheKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Run<CachedTranslation?>("read", connection => Read(connection, key.Value), fallback: null));
    }

    public Task SetAsync(TranslationCacheKey key, CachedTranslation translation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Run(
            "write",
            connection =>
            {
                Upsert(connection, key.Value, translation);
                PruneIfDue(connection);
                return true;
            },
            fallback: false);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disabled = true;
            _connection?.Dispose();
            _connection = null;
        }
    }

    // Every operation goes through here. Anything that goes wrong becomes the fallback value, a
    // damaged file is replaced and the operation tried once more on the new one, and a file that
    // keeps failing is given up on.
    private T Run<T>(string operation, Func<SqliteConnection, T> work, T fallback)
    {
        lock (_gate)
        {
            if (TryWork(operation, work, out T result, out bool fileReplaced))
            {
                return result;
            }

            // The file was damaged and has just been replaced by an empty one: one more try on it.
            return fileReplaced && TryWork(operation, work, out result, out _) ? result : fallback;
        }
    }

    private bool TryWork<T>(string operation, Func<SqliteConnection, T> work, out T result, out bool fileReplaced)
    {
        result = default!;
        fileReplaced = false;

        if (_disabled)
        {
            return false;
        }

        try
        {
            SqliteConnection connection = _connection ??= OpenAndPrepare();
            result = work(connection);
            _failures = 0;
            return true;
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            fileReplaced = HandleFailure(operation, exception);
            return false;
        }
    }

    private bool HandleFailure(string operation, Exception exception)
    {
        if (exception is SqliteException { SqliteErrorCode: SqliteCorrupt or SqliteNotADatabase } && _recoveries < MaxRecoveries)
        {
            _recoveries++;
            DiscardFiles();
            logger.PersistentCacheRecreated();
            return true;
        }

        _failures++;
        logger.PersistentCacheFailed(operation, exception);

        if (_failures >= MaxConsecutiveFailures)
        {
            _disabled = true;
            DiscardConnection();
            logger.PersistentCacheDisabled();
        }

        return false;
    }

    private SqliteConnection OpenAndPrepare()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        SqliteConnection connection = new(_connectionString);

        try
        {
            connection.Open();

            // WAL keeps writes cheap, and NORMAL is safe with it. Losing the last few entries in a
            // crash is no loss at all for a cache.
            Execute(connection, "PRAGMA journal_mode = WAL;");
            Execute(connection, "PRAGMA synchronous = NORMAL;");

            if (Convert.ToInt64(Scalar(connection, "PRAGMA user_version;")) != SchemaVersion)
            {
                CreateSchema(connection);
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    // Also what happens to a file left by another version: its contents are only ever a copy of
    // work that can be redone, so it is dropped rather than migrated.
    private static void CreateSchema(SqliteConnection connection)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();

        Execute(connection, "DROP TABLE IF EXISTS translations;", transaction);
        Execute(
            connection,
            """
            CREATE TABLE translations (
                key TEXT NOT NULL PRIMARY KEY,
                text TEXT NOT NULL,
                provider_id TEXT NOT NULL,
                provider_revision TEXT NOT NULL,
                quality_tier INTEGER NOT NULL,
                last_used INTEGER NOT NULL
            ) WITHOUT ROWID;
            """,
            transaction);
        Execute(connection, "CREATE INDEX ix_translations_last_used ON translations (last_used);", transaction);
        Execute(connection, $"PRAGMA user_version = {SchemaVersion};", transaction);

        transaction.Commit();
    }

    private CachedTranslation? Read(SqliteConnection connection, string key)
    {
        CachedTranslation? found;

        using (SqliteCommand select = connection.CreateCommand())
        {
            select.CommandText =
                "SELECT text, provider_id, provider_revision, quality_tier FROM translations WHERE key = $key;";
            select.Parameters.AddWithValue("$key", key);

            using SqliteDataReader reader = select.ExecuteReader();
            found = reader.Read()
                ? new CachedTranslation
                {
                    Text = reader.GetString(0),
                    ProviderId = reader.GetString(1),
                    ProviderRevision = reader.GetString(2),
                    QualityTier = reader.GetInt32(3),
                }
                : null;
        }

        if (found is not null)
        {
            long now = _time.GetUtcNow().ToUnixTimeSeconds();

            using SqliteCommand touch = connection.CreateCommand();
            touch.CommandText = "UPDATE translations SET last_used = $now WHERE key = $key AND last_used < $stale;";
            touch.Parameters.AddWithValue("$now", now);
            touch.Parameters.AddWithValue("$key", key);
            touch.Parameters.AddWithValue("$stale", now - TouchThresholdSeconds);
            touch.ExecuteNonQuery();
        }

        return found;
    }

    // The WHERE on the conflict branch is CachedTranslation.ShouldReplace, written where it cannot
    // be raced: an existing translation of a better tier is left alone, anything else is replaced.
    private void Upsert(SqliteConnection connection, string key, CachedTranslation translation)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO translations (key, text, provider_id, provider_revision, quality_tier, last_used)
            VALUES ($key, $text, $provider, $revision, $tier, $now)
            ON CONFLICT (key) DO UPDATE SET
                text = excluded.text,
                provider_id = excluded.provider_id,
                provider_revision = excluded.provider_revision,
                quality_tier = excluded.quality_tier,
                last_used = excluded.last_used
            WHERE excluded.quality_tier >= translations.quality_tier;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$text", translation.Text);
        command.Parameters.AddWithValue("$provider", translation.ProviderId);
        command.Parameters.AddWithValue("$revision", translation.ProviderRevision);
        command.Parameters.AddWithValue("$tier", translation.QualityTier);
        command.Parameters.AddWithValue("$now", _time.GetUtcNow().ToUnixTimeSeconds());
        command.ExecuteNonQuery();
    }

    // Counting every write would be wasteful; every so often is enough, because the cap is a
    // ceiling to stay near, not a promise to the last entry.
    private void PruneIfDue(SqliteConnection connection)
    {
        if (++_writesSincePrune < pruneEvery)
        {
            return;
        }

        _writesSincePrune = 0;

        long excess = Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM translations;")) - maxEntries;
        if (excess <= 0)
        {
            return;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM translations WHERE key IN (SELECT key FROM translations ORDER BY last_used ASC LIMIT $excess);";
        command.Parameters.AddWithValue("$excess", excess);
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.ExecuteNonQuery();
    }

    private void DiscardConnection()
    {
        _connection?.Dispose();
        _connection = null;
    }

    private void DiscardFiles()
    {
        DiscardConnection();

        foreach (string file in new[] { path, path + "-wal", path + "-shm" })
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Whatever cannot be deleted stays; the next failure will say so.
            }
        }
    }
}