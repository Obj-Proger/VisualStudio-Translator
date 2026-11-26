using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using VisualStudioTranslator.Vsix.Options;

namespace VisualStudioTranslator.Vsix.QuickInfo;

// Every kind of file: the translation reads what the other sources put in the tooltip, not the code, so it does
// not care which language the code is in, nor who wrote the tooltip.
[Export(typeof(IAsyncQuickInfoSourceProvider))]
[Name("Visual Studio Translator Quick Info")]
[ContentType("any")]
[method: ImportingConstructor]
internal sealed class TranslatedQuickInfoSourceProvider(
    IViewElementFactoryService viewElements, IViewTagAggregatorFactoryService tagAggregators) : IAsyncQuickInfoSourceProvider
{
    public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer) =>
        new TranslatedQuickInfoSource(textBuffer, viewElements, tagAggregators);
}

/// <summary>
/// Adds the translation of a tooltip's text to the tooltip Visual Studio shows on hover. What the other sources
/// show is not known until they have all answered, so this source answers at once with an empty element, which
/// <see cref="TooltipTranslation"/> fills in once the tooltip is on screen, if it has text to translate. Whatever goes
/// wrong, the original tooltip is unaffected: this source answers with nothing rather than failing.
/// </summary>
internal sealed class TranslatedQuickInfoSource(
    ITextBuffer buffer, IViewElementFactoryService viewElements, IViewTagAggregatorFactoryService tagAggregators)
    : IAsyncQuickInfoSource
{
    private const string Source = nameof(TranslatedQuickInfoSource);

    public async Task<QuickInfoItem?> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
    {
        try
        {
            // A view showing several buffers (a Razor page, say) has a source for each, and the tooltip needs one
            // translation, not one per buffer: the view's own buffer answers.
            if (session.TextView.TextBuffer != buffer)
            {
                return null;
            }

            TranslatorSettings settings = await TranslatorOptions.Shared.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (settings.ResolvedTargetLanguage is null)
            {
                return null;
            }

            // WPF elements belong to the thread that made them, and the tooltip is drawn on the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            IReadOnlyList<object> untranslated = settings.TranslateErrors ? [] : ErrorsAt(session);
            TooltipTranslation translation = new(viewElements, session, untranslated);

            // No span of its own: the tooltip covers what the other sources say it covers.
            return new QuickInfoItem(null, translation.Element);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ActivityLog.LogError(Source, $"Quick Info failed: {ex}");
            return null;
        }
    }

    public void Dispose()
    {
    }

    // What the errors and warnings under the mouse show in a tooltip: the squiggles' own tooltip content, which is
    // what the tooltip draws for them.
    private List<object> ErrorsAt(IAsyncQuickInfoSession session)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        SnapshotPoint? point = session.GetTriggerPoint(session.TextView.TextBuffer.CurrentSnapshot);
        if (point is null)
        {
            return [];
        }

        SnapshotSpan span = new(point.Value, point.Value.Position < point.Value.Snapshot.Length ? 1 : 0);
        using ITagAggregator<IErrorTag> errors = tagAggregators.CreateTagAggregator<IErrorTag>(session.TextView);

        return [.. errors.GetTags(span).Select(tag => tag.Tag.ToolTipContent).OfType<object>()];
    }
}