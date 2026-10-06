using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VisualStudioTranslator.Engine.Logging;

/// <summary>
/// Writes the Engine's log to a file. The Engine runs without a window, so its console output goes
/// nowhere, and without this there would be no way to find out what it did. Messages never carry
/// the text being translated (the logging code is written that way throughout), so the file is
/// safe to share when something goes wrong.
/// <para>
/// Deliberately plain: each line is appended and the file closed again, so nothing is held open and
/// rotation is a handful of renames. When the file reaches <paramref name="maxBytes"/> it becomes
/// "engine.1.log", the previous "engine.1.log" becomes "engine.2.log", and so on, up to
/// <paramref name="keepFiles"/> of them, the oldest being dropped. Logging must never break what it observes, so
/// a failure to write is ignored.
/// </para>
/// </summary>
internal sealed class FileLoggerProvider(
    string path,
    long maxBytes = 1_000_000,
    int keepFiles = 3,
    TimeProvider? time = null) : ILoggerProvider
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Lock _gate = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualStudioTranslator",
        "logs",
        "engine.log");

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        string timestamp = _time.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        string line = $"{timestamp} {LevelName(level)} {ShortName(category)}: {message}{Environment.NewLine}";

        if (exception is not null)
        {
            line += exception + Environment.NewLine;
        }

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                RotateIfNeeded();
                File.AppendAllText(path, line, Utf8WithoutBom);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nowhere to report that the log itself could not be written.
            }
        }
    }

    private void RotateIfNeeded()
    {
        FileInfo current = new(path);

        if (!current.Exists || current.Length < maxBytes)
        {
            return;
        }

        // The oldest goes, then each one moves up a place, and the current file becomes number 1.
        File.Delete(NumberedPath(keepFiles));

        for (int i = keepFiles - 1; i >= 0; i--)
        {
            string from = NumberedPath(i);

            if (File.Exists(from))
            {
                File.Move(from, NumberedPath(i + 1), overwrite: true);
            }
        }
    }

    private string NumberedPath(int number) => number == 0
        ? path
        : Path.Combine(
            Path.GetDirectoryName(path)!,
            $"{Path.GetFileNameWithoutExtension(path)}.{number}{Path.GetExtension(path)}");

    private static string ShortName(string category) => category[(category.LastIndexOf('.') + 1)..];

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info ",
        LogLevel.Warning => "warn ",
        LogLevel.Error => "error",
        LogLevel.Critical => "crit ",
        _ => "     ",
    };

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                owner.Write(logLevel, category, formatter(state, exception), exception);
            }
        }
    }
}