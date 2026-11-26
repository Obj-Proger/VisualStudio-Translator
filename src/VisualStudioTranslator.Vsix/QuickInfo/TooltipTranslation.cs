using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Adornments;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Vsix.Resources;
using VisualStudioTranslator.Vsix.Translation;

namespace VisualStudioTranslator.Vsix.QuickInfo;

/// <summary>
/// The translation in one tooltip. It starts as an empty element; once the tooltip is on screen, and everything the
/// other sources show is known, it reads their lines (<see cref="TooltipContent"/>), and if any holds prose, shows the
/// strip saying a translation is on its way and asks for it. The translation replaces the strip in place, which
/// works because the tooltip keeps showing the very element it was given, so the user never has to hover again.
/// <para>
/// The lines go to the Engine as one documentation comment, a paragraph each, with the code kept out of the
/// translation; the same comment hovered again comes from the Engine's cache. Text already in the language asked
/// for comes back unchanged and is not shown twice.
/// </para>
/// </summary>
internal sealed class TooltipTranslation
{
    private const string Source = nameof(TooltipTranslation);

    private readonly IViewElementFactoryService _viewElements;
    private readonly IAsyncQuickInfoSession _session;
    private readonly IReadOnlyList<object> _untranslated;
    private readonly ContentControl _host;
    private bool _started;

    /// <param name="untranslated">Tooltip content whose lines are to be left as they are: the messages of errors and
    /// warnings, when the user wants those in the original.</param>
    public TooltipTranslation(
        IViewElementFactoryService viewElements, IAsyncQuickInfoSession session, IReadOnlyList<object> untranslated)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _viewElements = viewElements;
        _session = session;
        _untranslated = untranslated;
        _host = new ContentControl { Focusable = false, Visibility = Visibility.Collapsed };

        session.StateChanged += OnStateChanged;
    }

    /// <summary>What goes into the tooltip.</summary>
    public UIElement Element => _host;

    private void OnStateChanged(object sender, QuickInfoSessionStateChangedEventArgs e)
    {
        try
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (e.NewState == QuickInfoSessionState.Dismissed)
            {
                _session.StateChanged -= OnStateChanged;
            }
            else if (e.NewState == QuickInfoSessionState.Visible && !_started)
            {
                _started = true;
                Start();
            }
        }
        catch (Exception ex)
        {
            ActivityLog.LogError(Source, $"Reading the tooltip failed: {ex}");
        }
    }

    private void Start()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        List<object> content = [.. _session.Content];

        // Worked out again (the tooltip followed the mouse to another word), and this element is no longer in it.
        if (!content.Remove(_host))
        {
            _session.StateChanged -= OnStateChanged;
            return;
        }

        // Nothing else to show: the tooltip would be an empty box.
        if (content.Count == 0)
        {
            _session.StateChanged -= OnStateChanged;
            _ = _session.DismissAsync();
            return;
        }

        // The lines to leave are told by their text: a source may draw a copy of the content rather than the content.
        HashSet<string> leave = [.. TooltipContent.Read(_untranslated).Select(line => line.Text)];
        IReadOnlyList<TooltipLine> lines = TooltipContent.Read(content);
        List<TooltipLine> prose = [.. lines.Where(line => TooltipText.IsProse(line.Runs) && !leave.Contains(line.Text))];

        if (prose.Count == 0)
        {
            return;
        }

        _host.Content = Pending();
        _host.Visibility = Visibility.Visible;
        _ = TranslateAsync(lines, prose);
    }

    // Runs after the tooltip has been shown, so nothing may escape from it.
    private async Task TranslateAsync(IReadOnlyList<TooltipLine> lines, IReadOnlyList<TooltipLine> prose)
    {
        try
        {
            IReadOnlyList<IReadOnlyList<TooltipRun>> runs = [.. prose.Select(line => line.Runs)];
            string documentation = DocumentationXmlWriter.Write(TooltipText.ToDocument(runs));

            DocumentModel? translated = await DocumentationTranslator.Shared.TranslateAsync(documentation).ConfigureAwait(false);
            IReadOnlyList<TranslatedLine> result = translated is null
                ? []
                : Pair(lines, prose, TooltipText.FromDocument(runs, translated));

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // A tooltip that has closed in the meantime is left alone; the translation is in the Engine's cache, so
            // the next hover shows it at once.
            if (_session.State != QuickInfoSessionState.Dismissed)
            {
                Show(result);
            }
        }
        catch (Exception ex)
        {
            ActivityLog.LogError(Source, $"Translating the tooltip failed: {ex}");
        }
    }

    private void Show(IReadOnlyList<TranslatedLine> lines)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        UIElement? translation = lines.Count == 0 ? null : Render(TranslatedElements.Create(lines));
        _host.Content = translation;
        _host.Visibility = translation is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // The translated lines that differ from their originals, each with the caption it stood under.
    private static List<TranslatedLine> Pair(
        IReadOnlyList<TooltipLine> lines, IReadOnlyList<TooltipLine> prose, IReadOnlyList<IReadOnlyList<TranslatedPiece>?> translated)
    {
        List<TranslatedLine> result = [];

        for (int index = 0; index < prose.Count; index++)
        {
            if (translated[index] is not { } pieces)
            {
                continue;
            }

            TranslatedLine line = new(prose[index], pieces, CaptionOf(lines, prose[index]));

            if (!string.Equals(Normalize(line.Text), Normalize(line.Original.Text), StringComparison.Ordinal))
            {
                result.Add(line);
            }
        }

        return result;
    }

    // The line just above, when it is a caption such as "Returns:" rather than prose.
    private static TooltipLine? CaptionOf(IReadOnlyList<TooltipLine> lines, TooltipLine line)
    {
        int index = IndexOf(lines, line);
        TooltipLine? above = index > 0 ? lines[index - 1] : null;

        return above is not null && !TooltipText.IsProse(above.Runs) && above.Text.TrimEnd().EndsWith(":", StringComparison.Ordinal)
            ? above
            : null;
    }

    private static int IndexOf(IReadOnlyList<TooltipLine> lines, TooltipLine line)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            if (ReferenceEquals(lines[index], line))
            {
                return index;
            }
        }

        return -1;
    }

    private static string Normalize(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // The words in the grey of code the compiler leaves out, over the strip: present, but not yet the thing itself.
    private StackPanel Pending()
    {
        StackPanel pending = new();
        UIElement? words = Render(new ContainerElement(
            ContainerElementStyle.Stacked,
            new ClassifiedTextElement(new ClassifiedTextRun(
                PredefinedClassificationTypeNames.ExcludedCode, UiText.Translating, ClassifiedTextRunStyle.Italic))));

        if (words is not null)
        {
            pending.Children.Add(words);
        }

        pending.Children.Add(new TranslationProgress { Margin = new Thickness(0, 4, 0, 2) });
        return pending;
    }

    private UIElement? Render(ContainerElement element) => _viewElements.CreateViewElement<UIElement>(_session.TextView, element);
}