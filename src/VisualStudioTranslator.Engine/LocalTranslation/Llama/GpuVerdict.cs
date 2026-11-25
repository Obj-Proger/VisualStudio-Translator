using System.Globalization;

namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>
/// Remembers, across runs of the Engine, that the graphics card failed to run the model, so that the starts
/// that follow go straight to the processor instead of failing first and costing the user a minute each.
/// <para>
/// A failure is not taken as final. A card can fail for a reason that passes: a game holding its memory, a
/// driver being updated. So the card is left alone for a day after a first failure, and for twice as long
/// after each failure that follows, up to a month; one start that works on it forgets them all. A new build
/// of the server, known by its path, starts with a clean slate as well.
/// </para>
/// <para>
/// Losing the file costs one failed start, so nothing about reading or writing it may fail a translation.
/// </para>
/// </summary>
internal sealed class GpuVerdict(string path, TimeProvider time)
{
    private static readonly TimeSpan FirstPause = TimeSpan.FromDays(1);
    private static readonly TimeSpan LongestPause = TimeSpan.FromDays(30);

    /// <summary>Whether the card is to be left alone for now with the server at <paramref name="serverPath"/>.</summary>
    public bool IsUnusable(string serverPath) =>
        Read() is { } record
        && record.ServerPath.Equals(serverPath, StringComparison.OrdinalIgnoreCase)
        && time.GetUtcNow() < record.Until;

    /// <summary>Records one more failure, which leaves the card alone for longer than the one before.</summary>
    public void MarkUnusable(string serverPath)
    {
        int failures = Read() is { } record && record.ServerPath.Equals(serverPath, StringComparison.OrdinalIgnoreCase)
            ? record.Failures + 1
            : 1;

        TimeSpan pause = TimeSpan.FromTicks(Math.Min(LongestPause.Ticks, FirstPause.Ticks << Math.Min(failures - 1, 10)));

        Write(string.Join(
            "\n",
            serverPath,
            failures.ToString(CultureInfo.InvariantCulture),
            (time.GetUtcNow() + pause).ToString("O", CultureInfo.InvariantCulture)));
    }

    /// <summary>Forgets every failure: the card has just worked.</summary>
    public void MarkUsable()
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left behind, it runs out by itself.
        }
    }

    private Record? Read()
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string[] lines = File.ReadAllText(path).Split('\n');

            // Anything not in the shape written below is a file from elsewhere or half a file: it counts for nothing.
            return lines.Length == 3
                && int.TryParse(lines[1], NumberStyles.None, CultureInfo.InvariantCulture, out int failures)
                && DateTimeOffset.TryParseExact(
                    lines[2], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset until)
                ? new Record(lines[0], failures, until)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Write(string content)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not remembered: the next start tries the card again and falls back again.
        }
    }

    private sealed record Record(string ServerPath, int Failures, DateTimeOffset Until);
}