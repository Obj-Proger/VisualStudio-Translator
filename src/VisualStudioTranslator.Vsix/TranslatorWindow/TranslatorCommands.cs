using System.ComponentModel.Design;
using System.Globalization;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using Microsoft.VisualStudio.TextManager.Interop;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Vsix.Options;
using VisualStudioTranslator.Vsix.Resources;
using VisualStudioTranslator.Vsix.Service;

namespace VisualStudioTranslator.Vsix.TranslatorWindow;

/// <summary>
/// The commands that open the translator window, declared in <c>TranslatorWindow\Commands.vsct</c>: Translator in
/// View &gt; Other Windows, Translate Selection in the editor's context menu, and Translate Message in the Error
/// List's. The first two answer to Ctrl+Shift+Alt+T; in the editor it translates what is selected.
/// </summary>
internal static class TranslatorCommands
{
    public const string CommandSetString = "f65b69ac-b42e-45bf-8783-64d40fa8a22b";
    public const int ShowTranslatorId = 0x0100;
    public const int TranslateSelectionId = 0x0101;
    public const int TranslateErrorMessageId = 0x0102;

    private const string Source = nameof(TranslatorCommands);

    private static readonly Guid CommandSet = new(CommandSetString);

    public static async Task InitializeAsync(AsyncPackage package, CancellationToken cancellationToken)
    {
        object? service = await package.GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(false);
        object? errorList = await package.GetServiceAsync(typeof(SVsErrorList)).ConfigureAwait(false);

        await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        // Visual Studio's language, from the one thread whose culture is that language, before any window shows a word.
        UiText.Culture = CultureInfo.CurrentUICulture;

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

        OleMenuCommand translateMessage = new(
            (_, _) => Run(package, () => TranslateErrorMessagesAsync(package, errorList as IErrorList)),
            new CommandID(CommandSet, TranslateErrorMessageId));
        translateMessage.BeforeQueryStatus += (_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            translateMessage.Enabled = errorList is IErrorList list && SelectedMessages(list).Count > 0;
        };
        commands.AddCommand(translateMessage);
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

    private static async Task TranslateErrorMessagesAsync(AsyncPackage package, IErrorList? errorList)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        IReadOnlyList<string> messages = errorList is null ? [] : SelectedMessages(errorList);

        // Several entries selected: one message a paragraph, in the order the list shows them.
        await ShowAsync(package, messages.Count == 0 ? null : string.Join("\n\n", messages)).ConfigureAwait(true);
    }

    // The text of each entry selected in the Error List, without its code ("CS0103"), which needs no translation.
    private static List<string> SelectedMessages(IErrorList errorList)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        List<string> messages = [];

        foreach (ITableEntryHandle entry in errorList.TableControl.SelectedEntries)
        {
            if (entry.TryGetValue(StandardTableKeyNames.Text, out object? value)
                && value is string text
                && !string.IsNullOrWhiteSpace(text))
            {
                messages.Add(text.Trim());
            }
        }

        return messages;
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