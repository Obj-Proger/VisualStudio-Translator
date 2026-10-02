using System.Text;

namespace VisualStudioTranslator.Engine.LocalTranslation.Bergamot;

/// <summary>
/// Builds the <c>config.yml</c> the engine needs next to a model's files. The registry that
/// models are downloaded from does not ship one, so it is derived from what is in the directory.
/// The settings are the ones the wrapper's documentation recommends for a CPU: greedy search
/// and 8-bit arithmetic, which is what keeps translation fast enough for a hover.
/// </summary>
internal static class BergamotModelConfig
{
    public const string FileName = "config.yml";

    /// <param name="fileNames">Names (not paths) of the files in the model directory.</param>
    /// <returns>The config text, or <see langword="null"/> when the files do not make up a complete model.</returns>
    public static string? Build(IEnumerable<string> fileNames)
    {
        List<string> files = [.. fileNames];

        string? model = Pick(files, "model.", ".bin");
        if (model is null)
        {
            return null;
        }

        string? sourceVocabulary = Pick(files, "srcvocab.", ".spm");
        string? targetVocabulary = Pick(files, "trgvocab.", ".spm");

        // Some models share one vocabulary between both sides; the engine still wants two entries.
        if (sourceVocabulary is null || targetVocabulary is null)
        {
            string? shared = Pick(files, "vocab.", ".spm");
            if (shared is null)
            {
                return null;
            }

            sourceVocabulary = shared;
            targetVocabulary = shared;
        }

        string? shortlist = Pick(files, "lex.", ".bin");

        // Models with learned quantization scales ("alphas" in the name) need the matching mode.
        string precision = model.Contains("alphas", StringComparison.OrdinalIgnoreCase)
            ? "int8shiftAlphaAll"
            : "int8shiftAll";

        StringBuilder yaml = new();
        yaml.Append("relative-paths: true\n");
        yaml.Append("models:\n- ").Append(model).Append('\n');
        yaml.Append("vocabs:\n- ").Append(sourceVocabulary).Append("\n- ").Append(targetVocabulary).Append('\n');

        if (shortlist is not null)
        {
            yaml.Append("shortlist:\n- ").Append(shortlist).Append("\n- false\n");
        }

        yaml.Append("beam-size: 1\n");
        yaml.Append("normalize: 1.0\n");
        yaml.Append("word-penalty: 0\n");
        yaml.Append("max-length-break: 128\n");
        yaml.Append("mini-batch-words: 1024\n");
        yaml.Append("workspace: 128\n");
        yaml.Append("max-length-factor: 2.0\n");
        yaml.Append("skip-cost: true\n");
        yaml.Append("cpu-threads: 0\n");
        yaml.Append("quiet: true\n");
        yaml.Append("quiet-translation: true\n");
        yaml.Append("gemm-precision: ").Append(precision).Append('\n');

        return yaml.ToString();
    }

    private static string? Pick(List<string> files, string prefix, string suffix) =>
        files
            .Where(file => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.Ordinal)
            .FirstOrDefault();
}