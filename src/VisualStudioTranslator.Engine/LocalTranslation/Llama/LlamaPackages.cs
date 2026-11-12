using VisualStudioTranslator.Engine.LocalTranslation.Downloads;

namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>A build of llama.cpp for Windows, as one zip archive of programs and libraries side by side.</summary>
/// <param name="Version">The build's name, which is also the name of its folder once installed.</param>
internal sealed record RuntimePackage(string Version, string ArchiveName, DownloadSource Source);

/// <summary>A model file the server loads.</summary>
/// <param name="Name">What the user is told it is.</param>
internal sealed record ModelPackage(string Name, string FileName, DownloadSource Source);

/// <summary>
/// Exactly what is installed, and from where. Every file is pinned to one build and one revision, with its size
/// and SHA-256 checksum, so what lands on the user's machine is what was tested here and nothing else. Moving
/// to a newer build or model is a change to this file.
/// <para>
/// The server is llama.cpp (MIT licence), from its own releases on GitHub. The models are Google's Gemma 4
/// (Apache 2.0 licence), quantized to 4 bits by mradermacher, from Hugging Face.
/// </para>
/// </summary>
internal static class LlamaCatalog
{
    public const string ServerFileName = "llama-server.exe";

    /// <summary>
    /// Below this much memory the light model is chosen. A machine sold with 16 GB reports a little less than
    /// that, so the line sits under it: such a machine still gets the better model, one with 8 or 12 GB does not.
    /// </summary>
    public const long LightModelMemoryLimit = 14L * 1024 * 1024 * 1024;

    /// <summary>The build for the processor alone, which runs on every x64 machine.</summary>
    public static RuntimePackage Runtime { get; } = new(
        "b12317",
        "llama-b12317-bin-win-cpu-x64.zip",
        new DownloadSource(
            new Uri("https://github.com/ggml-org/llama.cpp/releases/download/b12317/llama-b12317-bin-win-cpu-x64.zip"),
            25_084_543,
            "96e5008bde3367ef049a0f327e0674b149a4e3385af90b3f046cc7fe0301d322"));

    /// <summary>The better translations, at about 5 GB on disk and in memory.</summary>
    public static ModelPackage Quality { get; } = new(
        "Gemma 4 E4B",
        "gemma-4-E4B-it.Q4_K_M.gguf",
        new DownloadSource(
            new Uri(
                "https://huggingface.co/mradermacher/gemma-4-E4B-it-GGUF/resolve/"
                + "bccd4df108bbde6cd495f2594c17a5182bc59304/gemma-4-E4B-it.Q4_K_M.gguf"),
            5_335_290_304,
            "e5555564868766605415f0e1401feac79c9327333c051521934c90aea67df897"));

    /// <summary>Faster and half the memory, at some cost to the translations.</summary>
    public static ModelPackage Light { get; } = new(
        "Gemma 4 E2B",
        "gemma-4-E2B-it.Q4_K_M.gguf",
        new DownloadSource(
            new Uri(
                "https://huggingface.co/mradermacher/gemma-4-E2B-it-GGUF/resolve/"
                + "cb7ceaccb7b0973a17ab6d39fe6d2b11987ea8b5/gemma-4-E2B-it.Q4_K_M.gguf"),
            3_427_878_336,
            "7e6e3d87757ca0560a4b0be80dc0bb480bb793b749b317f7aeda81b5ea8260a0"));

    /// <summary>The hosts the files come from, as the user is told before agreeing to the download.</summary>
    public static string SourceHosts => $"{Quality.Source.Url.Host} and {Runtime.Source.Url.Host}";

    /// <summary>
    /// The model that suits a machine with this much memory, until the user can choose for themselves.
    /// Memory that cannot be told counts as little.
    /// </summary>
    public static ModelPackage ChooseModel(long totalMemoryBytes) =>
        totalMemoryBytes >= LightModelMemoryLimit ? Quality : Light;
}