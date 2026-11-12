using System.Globalization;
using System.Text;
using Microsoft.VisualStudio.Shell;

namespace VisualStudioTranslator.Vsix.Service;

/// <summary>
/// The extension's own log, in a file. Visual Studio's activity log is only complete once it has been
/// closed, which is no help when something does not happen while it is running. Like the Engine's log,
/// it never carries the documentation being translated: only languages, states and the reasons
/// something went wrong, so it is safe to send along with a bug report.
/// </summary>
internal static class DiagnosticLog
{
    private const long MaxBytes = 1_000_000;

    private static readonly object Sync = new();
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualStudioTranslator",
        "logs",
        "vsix.log");

    public static void Info(string source, string message) => Write("info ", source, message);

    public static void Error(string source, string message)
    {
        Write("error", source, message);
        ActivityLog.LogError(source, message);
    }

    private static void Write(string level, string source, string message)
    {
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        string line = $"{timestamp} {level} {source}: {message}{Environment.NewLine}";

        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                // One older file is kept, which is plenty for a log nobody reads until something is wrong.
                FileInfo current = new(FilePath);
                if (current.Exists && current.Length >= MaxBytes)
                {
                    string older = Path.ChangeExtension(FilePath, ".1.log");
                    File.Delete(older);
                    File.Move(FilePath, older);
                }

                File.AppendAllText(FilePath, line, Utf8WithoutBom);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never break what it observes, and there is nowhere to report that it failed.
            }
        }
    }
}