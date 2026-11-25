using System.ComponentModel.Design;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Vsix.Options;
using VisualStudioTranslator.Vsix.Service;

namespace VisualStudioTranslator.Vsix.TranslatorWindow;

/// <summary>
/// The commands that open the translator window, declared in <c>TranslatorWindow\Commands.vsct</c>: Translator in
/// View &gt; Other Windows, and Translate Selection in the editor's context menu. Both answer to Ctrl+Shift+Alt+T;
/// in the editor it translates what is selected.
/// </summary>
internal static class TranslatorCommands
{
    public const string CommandSetString = "f65b69ac-b42e-45bf-8783-64d40fa8a22b";
    public const int ShowTranslatorId = 0x0100;
    public const int TranslateSelectionId = 0x0101;

    private const string Source = nameof(TranslatorCommands);

    private static readonly Guid CommandSet = new(CommandSetString);

    public static async Task InitializeAsync(AsyncPackage package, CancellationToken cancellationToken)
    {
        object? service = await package.GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(false);

        await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        if (service is not OleMenuCommandService commands)
        {
            DiagnosticLog.Error(Source, "Visual Studio offered no command service; the translator window has no commands.");
            return;
        }

        commands.AddCommand(new MenuCommand(
            (_, _) => Run(package, () => ShowAsync(package, text: null)),
            new CommandID(CommandSet, ShowTranslatorId)));

        commands.AddCommand(new MenuCommand(
            (_, _) => Run(package, () => TranslateSelectionAsync(package)),
            new CommandID(CommandSet, TranslateSelectionId)));
    }

    private static void Run(AsyncPackage package, Func<Task> command) =>
        _ = package.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await command().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error(Source, $"A translator command failed: {ex}");
            }
        });

    private static async Task TranslateSelectionAsync(AsyncPackage package)
    {
        object? service = await package.GetServiceAsync(typeof(SVsTextManager)).ConfigureAwait(false);

        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        string? selected = null;

        if (service is IVsTextManager manager
            && manager.GetActiveView(1, null, out IVsTextView view) == VSConstants.S_OK
            && view.GetSelectedText(out string text) == VSConstants.S_OK)
        {
            selected = CommentText.Strip(text);
        }

        // Nothing selected: the window opens ready for the user to type, which is what the shortcut does elsewhere.
        await ShowAsync(package, string.IsNullOrWhiteSpace(selected) ? null : selected).ConfigureAwait(true);
    }

    private static async Task ShowAsync(AsyncPackage package, string? text)
    {
        TranslatorSettings settings = await TranslatorOptions.Shared.ReadAsync(package.DisposalToken).ConfigureAwait(true);

        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        ToolWindowPane window = await package.ShowToolWindowAsync(
            typeof(TranslatorToolWindow), 0, create: true, package.DisposalToken).ConfigureAwait(true);

        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        if (window is not TranslatorToolWindow translator)
        {
            return;
        }

        translator.View.ChooseLanguage(settings);

        if (text is null)
        {
            translator.View.FocusSource();
        }
        else
        {
            translator.View.Translate(text);
        }
    }
}