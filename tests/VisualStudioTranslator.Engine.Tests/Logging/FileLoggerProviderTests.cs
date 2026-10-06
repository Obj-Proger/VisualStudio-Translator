// These tests exercise the logger itself, with every level enabled, so the cost the rule warns about
// (arguments built even when logging is off) is not a concern here.
#pragma warning disable CA1873

using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Engine.Logging;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.Logging;

public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("vst-log-");

    public void Dispose() => _root.Delete(recursive: true);

    private string LogPath => Path.Combine(_root.FullName, "logs", "engine.log");

    private ILogger Logger(long maxBytes = 1_000_000, int keepFiles = 3, string? path = null)
    {
        FileLoggerProvider provider = new(path ?? LogPath, maxBytes, keepFiles);
        return LoggerFactory.Create(builder => builder.AddProvider(provider).SetMinimumLevel(LogLevel.Trace))
            .CreateLogger("VisualStudioTranslator.Engine.Tests.Sample");
    }

    [Fact]
    public void Log_WritesATimestampedLineWithLevelCategoryAndMessage()
    {
        Logger().LogInformation("Hello {Name}", "world");

        string line = File.ReadAllLines(LogPath).Single();

        line.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z info  Sample: Hello world$");
    }

    [Theory]
    [InlineData(LogLevel.Warning, "warn")]
    [InlineData(LogLevel.Error, "error")]
    [InlineData(LogLevel.Debug, "debug")]
    public void Log_NamesTheLevel(LogLevel level, string expected)
    {
        Logger().Log(level, "x");

        File.ReadAllText(LogPath).Should().Contain($"Z {expected}");
    }

    [Fact]
    public void Log_WithAnException_IncludesItsDetails()
    {
        Logger().LogError(new InvalidOperationException("something broke"), "It failed");

        string text = File.ReadAllText(LogPath);

        text.Should().Contain("It failed");
        text.Should().Contain("InvalidOperationException");
        text.Should().Contain("something broke");
    }

    [Fact]
    public void Log_CreatesTheFolderWhenItIsMissing()
    {
        Directory.Exists(Path.GetDirectoryName(LogPath)).Should().BeFalse();

        Logger().LogInformation("first");

        File.Exists(LogPath).Should().BeTrue();
    }

    [Fact]
    public void Log_AppendsRatherThanOverwriting()
    {
        ILogger logger = Logger();

        logger.LogInformation("one");
        logger.LogInformation("two");

        File.ReadAllLines(LogPath).Should().HaveCount(2);
    }

    [Fact]
    public void Log_NonAsciiText_IsWrittenAsItIs()
    {
        Logger().LogInformation("Сегмент {Id}", 7);

        File.ReadAllText(LogPath).Should().Contain("Сегмент 7");
    }

    [Fact]
    public void Log_PastTheSizeLimit_RotatesAndKeepsOnlyTheConfiguredNumberOfFiles()
    {
        ILogger logger = Logger(maxBytes: 300, keepFiles: 2);

        for (int i = 0; i < 40; i++)
        {
            logger.LogInformation("line number {Number} with some padding text", i);
        }

        string folder = Path.GetDirectoryName(LogPath)!;
        File.Exists(LogPath).Should().BeTrue();
        File.Exists(Path.Combine(folder, "engine.1.log")).Should().BeTrue();
        File.Exists(Path.Combine(folder, "engine.2.log")).Should().BeTrue();
        File.Exists(Path.Combine(folder, "engine.3.log")).Should().BeFalse();
    }

    [Fact]
    public void Log_NewestLinesAreInTheCurrentFile()
    {
        ILogger logger = Logger(maxBytes: 300, keepFiles: 2);

        for (int i = 0; i < 40; i++)
        {
            logger.LogInformation("line number {Number} with some padding text", i);
        }

        File.ReadAllText(LogPath).Should().Contain("line number 39");
    }

    [Fact]
    public async Task Log_FromManyThreads_LosesNoLines()
    {
        ILogger logger = Logger();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, 200),
            TestContext.Current.CancellationToken,
            (i, _) =>
            {
                logger.LogInformation("message {Number}", i);
                return ValueTask.CompletedTask;
            });

        File.ReadAllLines(LogPath).Should().HaveCount(200);
    }

    [Fact]
    public void Log_WhereTheFileCannotBeWritten_NeverThrows()
    {
        // A file where the folder should be.
        string blocker = Path.Combine(_root.FullName, "blocker");
        File.WriteAllText(blocker, "not a folder");
        ILogger logger = Logger(path: Path.Combine(blocker, "engine.log"));

        Action act = () => logger.LogInformation("nowhere to go");

        act.Should().NotThrow();
    }
}