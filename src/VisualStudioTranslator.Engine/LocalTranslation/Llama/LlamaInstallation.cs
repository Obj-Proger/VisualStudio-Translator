namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>
/// Where the server and the model live on this machine, and whether they are there. Everything is under one
/// folder of the user's local application data:
/// <code>
/// llama\runtime\&lt;build&gt;\llama-server.exe   the server and its libraries, one folder per build
/// llama\models\&lt;model&gt;.gguf               the model
/// llama\downloads\                         archives while they download
/// </code>
/// </summary>
internal sealed class LlamaInstallation(string root, RuntimePackage runtime, ModelPackage model)
{
    /// <summary>Folders that begin like this are a build being unpacked, not a build.</summary>
    public const string InstallingPrefix = ".installing-";

    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualStudioTranslator",
        "llama");

    public string Root => root;

    public RuntimePackage Runtime => runtime;

    public ModelPackage Model => model;

    public string RuntimesDirectory => Path.Combine(root, "runtime");

    public string RuntimeDirectory => Path.Combine(RuntimesDirectory, runtime.Version);

    public string ServerPath => Path.Combine(RuntimeDirectory, LlamaCatalog.ServerFileName);

    public string ModelPath => Path.Combine(root, "models", model.FileName);

    public string DownloadsDirectory => Path.Combine(root, "downloads");

    // The server is unpacked into a folder of its own and moved into place whole, and the model gets its final
    // name only once verified, so either file being there means its whole package is.
    public bool IsRuntimeInstalled => File.Exists(ServerPath);

    public bool IsModelInstalled => File.Exists(ModelPath);

    public bool IsInstalled => IsRuntimeInstalled && IsModelInstalled;

    /// <summary>The installation for this machine: the default folder, and the model its memory suits.</summary>
    public static LlamaInstallation ForThisMachine() => new(
        DefaultRoot,
        LlamaCatalog.Runtime,
        LlamaCatalog.ChooseModel(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes));
}