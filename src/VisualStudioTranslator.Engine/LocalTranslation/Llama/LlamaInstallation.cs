using VisualStudioTranslator.Core.Rpc;

namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>
/// Where the server and the model live on this machine, and whether they are there. Everything is under one
/// folder of the user's local application data:
/// <code>
/// llama\runtime\&lt;build&gt;\llama-server.exe   the server and its libraries, one folder per build and backend
/// llama\models\&lt;model&gt;.gguf               the model
/// llama\downloads\                         archives while they download
/// </code>
/// <para>
/// The model is the one the user chose and can change while the Engine runs (see <see cref="UseModel"/>); the
/// server stays the same.
/// </para>
/// </summary>
internal sealed class LlamaInstallation(string root, RuntimePackage runtime, ModelPackage model)
{
    private ModelPackage _model = model;
    /// <summary>Folders that begin like this are a build being unpacked, not a build.</summary>
    public const string InstallingPrefix = ".installing-";

    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualStudioTranslator",
        "llama");

    public string Root => root;

    public RuntimePackage Runtime => runtime;

    /// <summary>How much memory this machine has, as the choice of model goes by.</summary>
    public static long MachineMemory => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

    public ModelPackage Model => Volatile.Read(ref _model);

    public string RuntimesDirectory => Path.Combine(root, "runtime");

    public string RuntimeDirectory => Path.Combine(RuntimesDirectory, runtime.Folder);

    public string ServerPath => Path.Combine(RuntimeDirectory, LlamaCatalog.ServerFileName);

    public string ModelPath => Path.Combine(root, "models", Model.FileName);

    public string DownloadsDirectory => Path.Combine(root, "downloads");

    /// <summary>Where a graphics card that failed with the installed server is remembered (see <see cref="GpuVerdict"/>).</summary>
    public string GpuVerdictPath => Path.Combine(root, "gpu-unusable.txt");

    /// <summary>Where the order of the machine's graphics cards is remembered (see <see cref="GpuRanking"/>).</summary>
    public string GpuRankingPath => Path.Combine(root, "gpu-ranking.txt");

    // The server is unpacked into a folder of its own and moved into place whole, and the model gets its final
    // name only once verified, so either file being there means its whole package is.
    public bool IsRuntimeInstalled => File.Exists(ServerPath);

    public bool IsModelInstalled => File.Exists(ModelPath);

    public bool IsInstalled => IsRuntimeInstalled && IsModelInstalled;

    /// <summary>The installation for this machine: the default folder, and the model its memory suits.</summary>
    public static LlamaInstallation ForThisMachine() => new(
        DefaultRoot,
        LlamaCatalog.Runtime,
        LlamaCatalog.ChooseModel(LocalModelChoice.Automatic, MachineMemory));

    /// <summary>
    /// Makes another of the catalogue's models the one this installation is about: what is installed, what there
    /// is to download, and what the server loads. The file of the model before stays on disk, so going back to it
    /// costs no download.
    /// </summary>
    public void UseModel(ModelPackage chosen) => Volatile.Write(ref _model, chosen);
}