using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace VisualStudioTranslator.Vsix.TranslatorWindow;

/// <summary>
/// The translator window: View &gt; Other Windows &gt; Translator, Ctrl+Shift+Alt+T, or Translate Selection in the
/// editor's context menu. Opens docked with the Output window the first time, and where the user left it after that.
/// What it shows is in <see cref="TranslatorView"/>.
/// </summary>
[Guid(WindowGuidString)]
public sealed class TranslatorToolWindow : ToolWindowPane
{
    public const string WindowGuidString = "1fa96c58-7fbf-4b1c-be9d-edd4d54d5e48";

    public TranslatorToolWindow()
        : base(null)
    {
        Caption = "Translator";
        View = new TranslatorView();
        Content = View;
    }

    internal TranslatorView View { get; }

    // Also when Visual Studio brings the window back on start-up, before any command of the extension has run.
    public override void OnToolWindowCreated()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnToolWindowCreated();

        if (Package is AsyncPackage package)
        {
            View.Attach(package.JoinableTaskFactory, new TranslatorWindowState(package));
        }
    }
}