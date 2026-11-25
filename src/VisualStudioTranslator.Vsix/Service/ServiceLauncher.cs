using System.Diagnostics;
using System.Reflection;
using VisualStudioTranslator.Core.Deployment;

namespace VisualStudioTranslator.Vsix.Service;

/// <summary>
/// Starts the Engine process.
/// <list type="bullet">
/// <item>An installed extension carries the Engine in its <c>Engine</c> folder, with its own .NET runtime, so nothing
/// has to be installed for it. It runs from a copy under %LOCALAPPDATA% (see <see cref="EngineDeployment"/>), which
/// leaves the extension's files free for Visual Studio to update or remove.</item>
/// <item>A Debug build has no such folder: it runs the Engine project's own build output, whose path is written into
/// this assembly when it is built (the DevelopmentEnginePath metadata, which a packed build leaves out).</item>
/// </list>
/// </summary>
internal static class ServiceLauncher
{
    private const string DevelopmentEnginePathMetadataKey = "DevelopmentEnginePath";

    private static readonly string DeploymentsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisualStudioTranslator", "engine");

    public static Process Start()
    {
        string enginePath = ResolveEnginePath();

        ProcessStartInfo startInfo = new(enginePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(enginePath)!,
        };

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the Engine process.");
    }

    /// <summary>Folders an Engine this extension starts may run from, for checking who is at the other end of the pipe.</summary>
    public static bool IsOurEngine(string executablePath)
    {
        string full = Path.GetFullPath(executablePath);

        if (full.StartsWith(Path.GetFullPath(DeploymentsRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return DevelopmentEnginePath() is { } development
            && string.Equals(full, Path.GetFullPath(development), StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveEnginePath()
    {
        string packed = Path.Combine(Path.GetDirectoryName(typeof(ServiceLauncher).Assembly.Location)!, "Engine");

        if (Directory.Exists(packed))
        {
            string version = typeof(ServiceLauncher).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            return EngineDeployment.Deploy(packed, DeploymentsRoot, version);
        }

        string? development = DevelopmentEnginePath();

        if (development is null || !File.Exists(development))
        {
            throw new FileNotFoundException(
                "The Engine was found neither in the extension nor in a development build. " +
                "Build VisualStudioTranslator.Engine before debugging the Vsix project.",
                development ?? packed);
        }

        return development;
    }

    // Written in by VisualStudioTranslator.Vsix.csproj for a build that does not pack the Engine: an absolute path,
    // because once deployed this assembly runs from the experimental instance's extension folder, nowhere near the
    // repository.
    private static string? DevelopmentEnginePath()
    {
        string? path = typeof(ServiceLauncher).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == DevelopmentEnginePathMetadataKey)
            ?.Value;

        // An explicit check rather than string.IsNullOrEmpty, which net48 does not annotate for the compiler.
        return path is null || path.Length == 0 ? null : path;
    }
}