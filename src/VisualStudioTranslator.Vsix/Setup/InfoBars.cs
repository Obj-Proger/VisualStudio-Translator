using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VisualStudioTranslator.Vsix.Service;

namespace VisualStudioTranslator.Vsix.Setup;

/// <summary>A bar that has been shown, which can be taken down again.</summary>
internal sealed class InfoBarHandle(IVsInfoBarHost host, IVsInfoBarUIElement element, uint cookie)
{
    public async Task CloseAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        element.Unadvise(cookie);
        host.RemoveInfoBar(element);
        element.Close();
    }
}

/// <summary>
/// Shows the information bar Visual Studio puts above the editor: a message and some buttons. It is the
/// place for a question that deserves to be seen but must not block the user, which is exactly what asking
/// for permission to download something is.
/// </summary>
internal static class InfoBars
{
    private const string Source = nameof(InfoBars);

    /// <param name="message">What to say.</param>
    /// <param name="buttons">The labels of the buttons, left to right.</param>
    /// <param name="onButton">Called, off the UI thread, with the label of the button that was pressed.</param>
    /// <returns>The bar, or <see langword="null"/> when Visual Studio has nowhere to show one; the log says why.</returns>
    public static async Task<InfoBarHandle?> ShowAsync(string message, IReadOnlyList<string> buttons, Func<string, Task> onButton)
    {
        // The services are fetched first, on whatever thread that happens to be, and only then is the
        // main thread entered: awaiting the fetch can resume anywhere, and the objects below must only
        // be touched on the UI thread.
        object? shellService = await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(SVsShell));
        object? factoryService = await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(SVsInfoBarUIFactory));

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (shellService is not IVsShell shell)
        {
            DiagnosticLog.Info(Source, "No bar shown: the shell service is not available.");
            return null;
        }

        if (factoryService is not IVsInfoBarUIFactory factory)
        {
            DiagnosticLog.Info(Source, "No bar shown: the info bar factory service is not available.");
            return null;
        }

        int result = shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out object hostObject);

        if (hostObject is not IVsInfoBarHost host)
        {
            DiagnosticLog.Info(
                Source,
                $"No bar shown: the main window has no info bar host (GetProperty returned {result}, got {hostObject?.GetType().FullName ?? "nothing"}).");
            return null;
        }

        IVsInfoBarTextSpan[] spans = [new InfoBarTextSpan(message)];

        List<IVsInfoBarActionItem> actions = [];
        foreach (string label in buttons)
        {
            actions.Add(new InfoBarButton(label));
        }

        InfoBarModel model = new(spans, actions, KnownMonikers.StatusInformation, isCloseButtonVisible: true);

        IVsInfoBarUIElement? element = factory.CreateInfoBar(model);

        if (element is null)
        {
            DiagnosticLog.Info(Source, "No bar shown: the factory returned nothing.");
            return null;
        }

        element.Advise(new Events(onButton), out uint cookie);
        host.AddInfoBar(element);

        DiagnosticLog.Info(Source, "A bar was shown.");
        return new InfoBarHandle(host, element, cookie);
    }

    private sealed class Events(Func<string, Task> onButton) : IVsInfoBarUIEvents
    {
        public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
        {
        }

        public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string label = actionItem.Text;
            DiagnosticLog.Info(Source, $"The button '{label}' was pressed.");

            // Off the UI thread: what a button does may take a while and must not freeze Visual Studio.
            _ = Task.Run(() => onButton(label));
        }
    }
}