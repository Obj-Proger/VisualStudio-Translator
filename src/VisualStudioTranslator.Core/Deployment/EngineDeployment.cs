using System.Security.Cryptography;
using System.Text;

namespace VisualStudioTranslator.Core.Deployment;

/// <summary>
/// Puts a copy of the Engine that comes with the extension where it is run from:
/// <c>%LOCALAPPDATA%\VisualStudioTranslator\engine\&lt;version&gt;-&lt;stamp&gt;</c>.
/// <list type="bullet">
/// <item>Not run from the extension's own folder: a running process keeps its files open, and Visual Studio could
/// then neither update nor remove the extension.</item>
/// <item>The folder is named after the files that make it up (their paths, sizes and times), so a new build gets a
/// folder of its own even when the version number has not changed, and an unchanged one is not copied again.</item>
/// <item>Copied into a temporary folder and then renamed in one step, so a folder by the final name is always
/// complete, even when Visual Studio is closed halfway or two of them start at once.</item>
/// <item>Copies of other versions are removed; one still running is left for next time.</item>
/// </list>
/// </summary>
public static class EngineDeployment
{
    public const string ExecutableName = "VisualStudioTranslator.Engine.exe";

    private const string TemporaryPrefix = ".tmp-";

    // A copy left by a start that never finished is removed once surely nobody is still writing it.
    private static readonly TimeSpan AbandonedAfter = TimeSpan.FromHours(1);

    /// <summary>
    /// Makes sure the Engine in <paramref name="sourceFolder"/> has a copy under <paramref name="deploymentsRoot"/>,
    /// and returns the path of the copy's executable.
    /// </summary>
    public static string Deploy(string sourceFolder, string deploymentsRoot, string version)
    {
        if (!File.Exists(Path.Combine(sourceFolder, ExecutableName)))
        {
            throw new FileNotFoundException(
                "The extension's copy of the Engine is incomplete.", Path.Combine(sourceFolder, ExecutableName));
        }

        string name = FolderName(sourceFolder, version);
        string target = Path.Combine(deploymentsRoot, name);
        string executable = Path.Combine(target, ExecutableName);

        if (!File.Exists(executable))
        {
            Directory.CreateDirectory(deploymentsRoot);
            string temporary = Path.Combine(deploymentsRoot, TemporaryPrefix + Guid.NewGuid().ToString("N"));

            try
            {
                CopyFolder(sourceFolder, temporary);
                Directory.Move(temporary, target);
            }
            catch (IOException) when (File.Exists(executable))
            {
                // Another Visual Studio finished the same copy first; theirs is as good.
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        RemoveOthers(deploymentsRoot, keep: name);

        return executable;
    }

    /// <summary>The name of the folder for this version and these files.</summary>
    public static string FolderName(string sourceFolder, string version)
    {
        StringBuilder manifest = new();
        string root = Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        IEnumerable<string> files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (string file in files)
        {
            FileInfo info = new(file);
            manifest.Append(file.Substring(root.Length).ToUpperInvariant())
                .Append('|').Append(info.Length)
                .Append('|').Append(info.LastWriteTimeUtc.Ticks)
                .Append('\n');
        }

        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(manifest.ToString()));
        string stamp = string.Concat(hash.Take(6).Select(part => part.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));

        return $"{Sanitize(version)}-{stamp}";
    }

    private static string Sanitize(string version)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string clean = new(version.Select(character => Array.IndexOf(invalid, character) >= 0 ? '_' : character).ToArray());

        return clean.Length == 0 || clean.StartsWith(".", StringComparison.Ordinal) ? "v" + clean : clean;
    }

    private static void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (string directory in Directory.GetDirectories(source))
        {
            CopyFolder(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }

    private static void RemoveOthers(string deploymentsRoot, string keep)
    {
        foreach (string folder in Directory.GetDirectories(deploymentsRoot))
        {
            string name = Path.GetFileName(folder);

            if (string.Equals(name, keep, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.StartsWith(TemporaryPrefix, StringComparison.Ordinal)
                && DateTime.UtcNow - Directory.GetCreationTimeUtc(folder) < AbandonedAfter)
            {
                continue;
            }

            TryDelete(folder);
        }
    }

    // A copy whose Engine is still running cannot be deleted; it goes next time.
    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}