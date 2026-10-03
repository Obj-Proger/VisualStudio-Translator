using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VisualStudioTranslator.Vsix.Service;

namespace VisualStudioTranslator.Vsix;

/// <summary>
/// The extension's entry point. Loads in the background as soon as Visual Studio starts,
/// regardless of whether a solution is open. Its only job so far is to warm up the connection
/// to the Engine; the features themselves are MEF components that Visual Studio loads on its own.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[InstalledProductRegistration(
    "Visual Studio Translator",
    "Translates IDE tooltips, XML documentation, and comments in place, without touching your source files.",
    "1.0")]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class VisualStudioTranslatorPackage : AsyncPackage
{
    public const string PackageGuidString = "5f2c9f7f-3f2e-4a7f-9b0e-1a2b3c4d5e6f";

    protected override Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        ActivityLog.LogInformation(nameof(VisualStudioTranslatorPackage), "Package initialized.");

        // Starts the Engine and connects without waiting, so that by the first hover it is
        // ready. A failure here is logged and retried on first use, never fatal.
        EngineConnection.Shared.WarmUp();

        return Task.CompletedTask;
    }
}