using System.Security.Cryptography;
using System.Text;
using VisualStudioTranslator.Core.Languages;

namespace VisualStudioTranslator.Engine.LocalTranslation;

/// <summary>
/// Where translation models live on disk: one directory per direction, named after the
/// primary language of each side ("en-ru"). The store knows nothing about what is inside
/// them; recognizing and loading a model is the engine-specific factory's job.
/// </summary>
internal sealed class LocalModelStore(string rootDirectory)
{
    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualStudioTranslator",
        "models");

    /// <summary>
    /// Directories are keyed by primary language only, so "pt-BR" looks in "en-pt". The tags
    /// are validated ASCII, so the result can never point outside the root.
    /// </summary>
    public string DirectoryFor(LanguagePair pair) =>
        Path.Combine(rootDirectory, $"{PrimaryLanguage(pair.Source)}-{PrimaryLanguage(pair.Target)}");

    /// <summary>
    /// A short fingerprint of every installed model file (path and size), for use as the
    /// provider's revision in cache keys, so installing a different model retires translations
    /// the old one made. Config files are left out: one is generated from the model files on
    /// first use, and that must not look like a new model.
    /// </summary>
    public string ComputeRevision()
    {
        try
        {
            if (!Directory.Exists(rootDirectory))
            {
                return "none";
            }

            StringBuilder description = new();

            foreach (string path in Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal))
            {
                string? fileName = Path.GetFileName(path);
                if (string.Equals(fileName, "config.yml", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fileName, "config.txt", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                description
                    .Append(Path.GetRelativePath(rootDirectory, path).Replace('\\', '/'))
                    .Append('|')
                    .Append(new FileInfo(path).Length)
                    .Append('\n');
            }

            return description.Length == 0
                ? "none"
                : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(description.ToString())))[..12];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "unknown";
        }
    }

    private static string PrimaryLanguage(string tag)
    {
        int dash = tag.IndexOf('-');
        return dash < 0 ? tag : tag[..dash];
    }
}