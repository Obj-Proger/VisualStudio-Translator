using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace VisualStudioTranslator.Vsix;

/// <summary>
/// The extension's entry point. Loads in the background as soon as Visual Studio starts,
/// regardless of whether a solution is open, so its presence can be confirmed without
/// any other feature being wired up yet.
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
        return Task.CompletedTask;
    }
}