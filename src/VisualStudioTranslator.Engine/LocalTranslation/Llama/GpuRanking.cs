namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>
/// Remembers which of the machine's graphics cards runs the model fastest, as measured once, so that later starts
/// go to it straight away. The measurement holds for one build of the server and one set of cards: another build,
/// or a card added or taken away, is measured again.
/// <para>Losing the file costs one measurement, so nothing about reading or writing it may fail a start.</para>
/// </summary>
internal sealed class GpuRanking(string path)
{
    /// <returns><paramref name="devices"/> fastest first, or <see langword="null"/> when they have not been measured.</returns>
    public IReadOnlyList<GpuDevice>? Get(string serverPath, IReadOnlyList<GpuDevice> devices)
    {
        string[] lines;

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            lines = File.ReadAllText(path).Split('\n');
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (lines.Length != devices.Count + 1 || !lines[0].Equals(serverPath, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Matched by name, each card once, so two cards of the same model still come out as two.
        List<GpuDevice> remaining = [.. devices];
        List<GpuDevice> ranked = [];

        foreach (string name in lines.Skip(1))
        {
            GpuDevice? device = remaining.FirstOrDefault(candidate => candidate.Name == name);

            if (device is null)
            {
                return null;
            }

            remaining.Remove(device);
            ranked.Add(device);
        }

        return ranked;
    }

    public void Save(string serverPath, IReadOnlyList<GpuDevice> ranked)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Join("\n", [serverPath, .. ranked.Select(device => device.Name)]));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not remembered: the next run measures again.
        }
    }
}