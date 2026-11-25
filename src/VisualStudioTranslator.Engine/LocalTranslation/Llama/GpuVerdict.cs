namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>
/// Remembers, across runs of the Engine, that the graphics card could not run the model with a given build
/// of the server, so that every later start goes straight to the processor instead of failing first and
/// costing the user a minute. The build is known by the server's path, which names it, so a new build gets a
/// new chance; so does a new driver once the user deletes the file, which is the only thing it holds.
/// <para>
/// Losing it costs one failed start, so nothing about reading or writing it is allowed to fail a translation.
/// </para>
/// </summary>
internal sealed class GpuVerdict(string path)
{
    /// <summary>Whether the graphics card failed with the server at <paramref name="serverPath"/>.</summary>
    public bool IsUnusable(string serverPath)
    {
        try
        {
            return File.Exists(path) && string.Equals(File.ReadAllText(path).Trim(), serverPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void MarkUnusable(string serverPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, serverPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not remembered: the next start tries the graphics card again and falls back again.
        }
    }
}