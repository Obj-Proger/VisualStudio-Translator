using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Threading;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Vsix.Options;
using VisualStudioTranslator.Vsix.QuickInfo;
using VisualStudioTranslator.Vsix.Service;

namespace VisualStudioTranslator.Vsix.SignatureHelp;

// Every kind of file, as for Quick Info. The source adds no signatures: it is how the extension learns that a
// signature help popup is open, whichever language service fills it.
[Export(typeof(ISignatureHelpSourceProvider))]
[Name("Visual Studio Translator Signature Help")]
[Order(After = "default")]
[ContentType("any")]
internal sealed class SignatureHelpTranslationProvider : ISignatureHelpSourceProvider
{
    public ISignatureHelpSource TryCreateSignatureHelpSource(ITextBuffer textBuffer) => new SignatureHelpTranslationSource();
}

internal sealed class SignatureHelpTranslationSource : ISignatureHelpSource
{
    private const string Source = nameof(SignatureHelpTranslationSource);

    public void AugmentSignatureHelpSession(ISignatureHelpSession session, IList<ISignature> signatures)
    {
        try
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // One translation for the popup, however many buffers have a source asked for it.
            if (!session.Properties.ContainsProperty(typeof(SignatureHelpTranslation)))
            {
                session.Properties.AddProperty(typeof(SignatureHelpTranslation), new SignatureHelpTranslation(session));
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error(Source, $"Watching signature help failed: {ex}");
        }
    }

    public ISignature? GetBestMatch(ISignatureHelpSession session) => null;

    public void Dispose()
    {
    }
}

/// <summary>
/// The translation of one signature help popup, the one that shows a method's signatures and documentation while its
/// arguments are typed. Language services fill it in their own ways, and some keep its documentation to themselves,
/// so it is read off the screen, as tooltips drawn by an extension's own element are: once the popup has drawn its
/// text, and again whenever it redraws it for another signature or parameter. Runs in a colour or weight of their own
/// are code, so a signature stays as it is. There is no room in the popup for anything of the extension's, so the
/// translation always takes the place of the original lines, without a link back; where the lines cannot be found
/// exactly, the popup is left as it is.
/// </summary>
internal sealed class SignatureHelpTranslation
{
    private const string Source = nameof(SignatureHelpTranslation);

    private readonly ISignatureHelpSession _session;
    private IPopupIntellisensePresenter? _presenter;
    private ISignature? _signature;
    private DispatcherTimer? _readTimer;
    private bool _enabled;
    private int _generation;

    public SignatureHelpTranslation(ISignatureHelpSession session)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _session = session;
        session.PresenterChanged += OnPresenterChanged;
        session.SelectedSignatureChanged += OnSelectedSignatureChanged;
        session.Dismissed += OnDismissed;

        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            TranslatorSettings settings = await TranslatorOptions.Shared.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            _enabled = settings.TranslateTooltips && settings.ResolvedTargetLanguage is not null && !_session.IsDismissed;
            if (_enabled)
            {
                WatchPresenter();
                WatchSignature();
                ReadWhenDrawn();
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error(Source, $"Starting the signature help translation failed: {ex}");
        }
    }

    private void OnPresenterChanged(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        WatchPresenter();
        ReadWhenDrawn();
    }

    private void OnSelectedSignatureChanged(object sender, SelectedSignatureChangedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        WatchSignature();
        ReadWhenDrawn();
    }

    private void OnCurrentParameterChanged(object sender, CurrentParameterChangedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ReadWhenDrawn();
    }

    private void OnSurfaceElementChanged(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ReadWhenDrawn();
    }

    private void OnDismissed(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _enabled = false;
        StopReadTimer();
        _session.PresenterChanged -= OnPresenterChanged;
        _session.SelectedSignatureChanged -= OnSelectedSignatureChanged;
        _session.Dismissed -= OnDismissed;
        Watch(ref _presenter, null);
        Watch(ref _signature, null);
    }

    private void WatchPresenter() => Watch(ref _presenter, _session.Presenter as IPopupIntellisensePresenter);

    private void WatchSignature() => Watch(ref _signature, _session.SelectedSignature);

    private void Watch(ref IPopupIntellisensePresenter? watched, IPopupIntellisensePresenter? next)
    {
        if (watched is not null)
        {
            watched.SurfaceElementChanged -= OnSurfaceElementChanged;
        }

        watched = next;

        if (watched is not null)
        {
            watched.SurfaceElementChanged += OnSurfaceElementChanged;
        }
    }

    private void Watch(ref ISignature? watched, ISignature? next)
    {
        if (watched is not null)
        {
            watched.CurrentParameterChanged -= OnCurrentParameterChanged;
        }

        watched = next;

        if (watched is not null)
        {
            watched.CurrentParameterChanged += OnCurrentParameterChanged;
        }
    }

    // The popup redraws its text after a change, so it is read once everything waiting to be drawn has been.
    private void ReadWhenDrawn()
    {
        if (!_enabled)
        {
            return;
        }

        StopReadTimer();
        _readTimer = new DispatcherTimer(TimeSpan.Zero, DispatcherPriority.ContextIdle, OnDrawn, Dispatcher.CurrentDispatcher);
    }

    private void OnDrawn(object? sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        StopReadTimer();

        try
        {
            if (!_enabled || _session.IsDismissed || _presenter?.SurfaceElement is not { } surface)
            {
                return;
            }

            IReadOnlyList<TooltipLine> lines = ScreenText.Read(TooltipVisuals.Within(surface), [], codeByLook: true);
            List<TooltipLine> prose = [.. lines.Where(line => TooltipText.IsProse(line.Runs))];

            if (prose.Count > 0)
            {
                _ = TranslateAsync(surface, lines, prose, ++_generation);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error(Source, $"Reading signature help failed: {ex}");
        }
    }

    private async Task TranslateAsync(
        UIElement surface, IReadOnlyList<TooltipLine> lines, IReadOnlyList<TooltipLine> prose, int generation)
    {
        try
        {
            IReadOnlyList<TranslatedLine> translated = await LineTranslation.TranslateAsync(lines, prose).ConfigureAwait(false);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // A popup that has closed, or redrawn its text since, is left alone: what was read is no longer shown.
            if (generation != _generation || !_enabled || _session.IsDismissed || translated.Count == 0)
            {
                return;
            }

            if (InPlaceTranslation.TryApplyWithin(surface, translated) is null)
            {
                DiagnosticLog.Info(Source, "The signature help's lines could not be found exactly; it is left as it is.");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error(Source, $"Translating signature help failed: {ex}");
        }
    }

    private void StopReadTimer()
    {
        _readTimer?.Stop();
        _readTimer = null;
    }
}