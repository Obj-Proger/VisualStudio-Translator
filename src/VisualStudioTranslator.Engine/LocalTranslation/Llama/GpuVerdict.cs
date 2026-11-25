using System.Globalization;

namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>
/// Remembers, across runs of the Engine, which graphics cards failed to run the model, so that the starts that
/// follow go straight to the next card, or the processor, instead of failing first and costing the user a minute.
/// <para>
/// A failure is not taken as final. A card can fail for a reason that passes: a game holding its memory, a
/// driver being updated. So a card is left alone for a day after a first failure, and for twice as long after
/// each failure that follows, up to a month; one start that works on it forgets them all. Each card is judged
/// on its own, and a new build of the server, known by its path, starts with a clean slate.
/// </para>
/// <para>
/// Losing the file costs one failed start, so nothing about reading or writing it may fail a translation.
/// </para>
/// </summary>
internal sealed class GpuVerdict(string path, TimeProvider time)
{
    private static readonly TimeSpan FirstPause = TimeSpan.FromDays(1);
    private static readonly TimeSpan LongestPause = TimeSpan.FromDays(30);

    private readonly Lock _sync = new();

    /// <summary>
    /// Whether <paramref name="device"/> is to be left alone for now with the server at <paramref name="serverPath"/>.
    /// </summary>
    /// <param name="device">The card's name; empty for "whichever card the server picks".</param>
    public bool IsUnusable(string serverPath, string device = "")
    {
        lock (_sync)
        {
            return Find(Read(), serverPath, device) is { } record && time.GetUtcNow() < record.Until;
        }
    }

    /// <summary>Records one more failure of the card, which leaves it alone for longer than the one before.</summary>
    public void MarkUnusable(string serverPath, string device = "")
    {
        lock (_sync)
        {
            List<Record> records = Read();
            Record? previous = Find(records, serverPath, device);
            int failures = previous is null ? 1 : previous.Failures + 1;
            TimeSpan pause = TimeSpan.FromTicks(Math.Min(LongestPause.Ticks, FirstPause.Ticks << Math.Min(failures - 1, 10)));

            // Records of other builds are of no further use.
            records.RemoveAll(record =>
                record == previous || !record.ServerPath.Equals(serverPath, StringComparison.OrdinalIgnoreCase));
            records.Add(new Record(serverPath, device, failures, time.GetUtcNow() + pause));
            Write(records);
        }
    }

    /// <summary>Forgets every failure of the card: it has just worked.</summary>
    public void MarkUsable(string serverPath, string device = "")
    {
        lock (_sync)
        {
            List<Record> records = Read();

            if (records.RemoveAll(record => Matches(record, serverPath, device)) > 0)
            {
                Write(records);
            }
        }
    }

    private static Record? Find(List<Record> records, string serverPath, string device) =>
        records.FirstOrDefault(record => Matches(record, serverPath, device));

    private static bool Matches(Record record, string serverPath, string device) =>
        record.ServerPath.Equals(serverPath, StringComparison.OrdinalIgnoreCase) && record.Device == device;

    // One card a line: server, card, failures, until. Anything not in that shape counts for nothing.
    private List<Record> Read()
    {
        List<Record> records = [];

        try
        {
            if (!File.Exists(path))
            {
                return records;
            }

            foreach (string line in File.ReadAllText(path).Split('\n'))
            {
                string[] fields = line.Split('\t');

                if (fields.Length == 4
                    && int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int failures)
                    && DateTimeOffset.TryParseExact(
                        fields[3], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset until))
                {
                    records.Add(new Record(fields[0], fields[1], failures, until));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Unreadable: as if nothing had failed.
        }

        return records;
    }

    private void Write(List<Record> records)
    {
        try
        {
            if (records.Count == 0)
            {
                File.Delete(path);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Join("\n", records.Select(record => string.Join(
                "\t",
                record.ServerPath,
                record.Device,
                record.Failures.ToString(CultureInfo.InvariantCulture),
                record.Until.ToString("O", CultureInfo.InvariantCulture)))));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not remembered: the next start tries the card again and falls back again.
        }
    }

    private sealed record Record(string ServerPath, string Device, int Failures, DateTimeOffset Until);
}