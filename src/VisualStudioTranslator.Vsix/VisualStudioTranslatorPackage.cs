using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VisualStudioTranslator.Vsix.Options;
using VisualStudioTranslator.Vsix.Service;
using VisualStudioTranslator.Vsix.TranslatorWindow;

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

// Tools > Options. The new settings experience (Unified Settings, on in Visual Studio 2026) shows the settings
// declared in registration.json and hides the classic page, which serves wherever it is off.
[ProvideSettingsManifest(PackageRelativeManifestFile = @"Options\registration.json")]
[ProvideOptionPage(typeof(GeneralOptionsPage), "Visual Studio Translator", "General", 0, 0, true, IsInUnifiedSettings = true)]

// The translator window and the commands that open it (TranslatorWindow\Commands.vsct). It first opens as a tab
// beside the Output window.
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(TranslatorToolWindow), Style = VsDockStyle.Tabbed, Window = OutputWindowGuid)]
public sealed class VisualStudioTranslatorPackage : AsyncPackage
{
    public const string PackageGuidString = "5f2c9f7f-3f2e-4a7f-9b0e-1a2b3c4d5e6f";

    private const string OutputWindowGuid = "34E76E81-EE4A-11D0-AE2E-00A0C90FFFC3";

    /// <summary>The classic settings page, loaded with its saved values. Only on the UI thread.</summary>
    internal GeneralOptionsPage GeneralOptions
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return (GeneralOptionsPage)GetDialogPage(typeof(GeneralOptionsPage));
        }
    }

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await TranslatorOptions.Shared.AttachAsync(this).ConfigureAwait(false);
        await TranslatorCommands.InitializeAsync(this, cancellationToken).ConfigureAwait(false);

        ActivityLog.LogInformation(nameof(VisualStudioTranslatorPackage), "Package initialized.");

        // Starts the Engine and connects without waiting, so that by the first hover it is
        // ready. A failure here is logged and retried on first use, never fatal.
        EngineConnection.Shared.WarmUp();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            EngineConnection.Shared.Dispose();
        }

        base.Dispose(disposing);
    }
}