using System.Text.Json;
using System.Text.RegularExpressions;
using VisualStudioTranslator.Core.Languages;

namespace VisualStudioTranslator.Engine.LocalTranslation.Bergamot;

/// <summary>One file of a model as the registry lists it: where it is, and any checksums it carries.</summary>
internal sealed record RegistryFile(string Path, IReadOnlyList<string> Sha256Hashes);

/// <summary>The model chosen for a direction.</summary>
internal sealed record RegistryModel(Uri BaseUrl, string Direction, string Architecture, IReadOnlyList<RegistryFile> Files);

/// <summary>
/// Finds the model for a language direction in the registry Mozilla publishes. Nothing about which
/// languages exist is written here: whatever the registry lists is what can be installed, so a language
/// Mozilla adds tomorrow works without a change.
/// <para>
/// The registry is read leniently, by name, because its layout is not documented anywhere stable: a
/// field this code does not know is ignored, and an entry that is not complete is skipped
/// in favour of the next, never an error.
/// </para>
/// </summary>
internal static partial class RegistryParser
{
    private static readonly string[] FileKinds = ["model", "vocab", "srcVocab", "trgVocab", "lexicalShortlist"];

    // Best quality first. "base" is the strongest of the compact models, "tiny" the weakest.
    private static readonly string[] ArchitecturePreference = ["base", "base-memory", "tiny"];

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Pattern();

    // A path that goes into a URL and into a local file name. Strict on purpose: it comes from the network.
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._/-]*$")]
    private static partial Regex SafePathPattern();

    public static RegistryModel? Find(JsonElement registry, LanguagePair pair)
    {
        if (registry.ValueKind != JsonValueKind.Object
            || !registry.TryGetProperty("baseUrl", out JsonElement baseUrlElement)
            || baseUrlElement.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(baseUrlElement.GetString(), UriKind.Absolute, out Uri? baseUrl)
            || !registry.TryGetProperty("models", out JsonElement models)
            || models.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (string direction in Directions(pair, models))
        {
            if (models.TryGetProperty(direction, out JsonElement candidates) && candidates.ValueKind == JsonValueKind.Array)
            {
                RegistryModel? model = Choose(baseUrl, direction, candidates);

                if (model is not null)
                {
                    return model;
                }
            }
        }

        return null;
    }

    // The registry names a direction by language, with the script appended for languages that
    // have more than one ("en-zh_hans"). The most specific name that matches comes first.
    private static IEnumerable<string> Directions(LanguagePair pair, JsonElement models)
    {
        string source = Primary(pair.Source);
        string[] targetParts = pair.Target.Split('-');
        string target = targetParts[0];
        string? script = targetParts.Length > 1 && targetParts[1].Length == 4 ? targetParts[1].ToLowerInvariant() : null;

        if (script is not null)
        {
            yield return $"{source}-{target}_{script}";
        }

        yield return $"{source}-{target}";

        string prefix = $"{source}-{target}_";
        foreach (string name in models.EnumerateObject()
            .Select(property => property.Name)
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal))
        {
            yield return name;
        }
    }

    // Releases are preferred to anything else, and among those the strongest architecture; a candidate
    // that turns out to be incomplete is passed over for the next one.
    private static RegistryModel? Choose(Uri baseUrl, string direction, JsonElement candidates)
    {
        List<(JsonElement Element, string Architecture, bool IsRelease)> all = [];

        foreach (JsonElement element in candidates.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            all.Add((
                element,
                GetString(element, "architecture") ?? string.Empty,
                string.Equals(GetString(element, "releaseStatus"), "Release", StringComparison.Ordinal)));
        }

        IEnumerable<(JsonElement Element, string Architecture, bool IsRelease)> pool =
            all.Any(candidate => candidate.IsRelease) ? all.Where(candidate => candidate.IsRelease) : all;

        foreach ((JsonElement element, string architecture, _) in pool.OrderBy(candidate => ArchitectureRank(candidate.Architecture)))
        {
            IReadOnlyList<RegistryFile>? files = ReadFiles(element);

            if (files is not null)
            {
                return new RegistryModel(baseUrl, direction, architecture, files);
            }
        }

        return null;
    }

    // A model needs its weights and a vocabulary, shared or one per side; the shortlist is optional.
    private static IReadOnlyList<RegistryFile>? ReadFiles(JsonElement candidate)
    {
        if (!candidate.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        Dictionary<string, RegistryFile> found = [];

        foreach (string kind in FileKinds)
        {
            if (files.TryGetProperty(kind, out JsonElement file)
                && file.ValueKind == JsonValueKind.Object
                && GetString(file, "path") is { } path
                && IsSafePath(path))
            {
                found[kind] = new RegistryFile(path, ReadHashes(file));
            }
        }

        bool complete = found.ContainsKey("model")
            && (found.ContainsKey("vocab") || (found.ContainsKey("srcVocab") && found.ContainsKey("trgVocab")));

        return complete ? [.. found.Values] : null;
    }

    // Any string in the file's record that looks like a SHA-256 is taken to be one: the registry's
    // field names are not something to depend on, and a checksum that is not there is simply not checked.
    private static List<string> ReadHashes(JsonElement file) =>
    [
        .. file.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String
                && Sha256Pattern().IsMatch(property.Value.GetString()!))
            .Select(property => property.Value.GetString()!.ToLowerInvariant())
            .Distinct(),
    ];

    private static bool IsSafePath(string path) => SafePathPattern().IsMatch(path) && !path.Contains("..", StringComparison.Ordinal);

    private static int ArchitectureRank(string architecture)
    {
        int index = Array.IndexOf(ArchitecturePreference, architecture);
        return index >= 0 ? index : ArchitecturePreference.Length;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Primary(string tag)
    {
        int dash = tag.IndexOf('-');
        return dash < 0 ? tag : tag[..dash];
    }
}