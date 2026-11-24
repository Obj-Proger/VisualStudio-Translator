using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
using VisualStudioTranslator.Core.Concurrency;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Vsix.Symbols;
using VisualStudioTranslator.Vsix.Translation;

namespace VisualStudioTranslator.Vsix.QuickInfo;

[Export(typeof(IAsyncQuickInfoSourceProvider))]
[Name("Visual Studio Translator Quick Info")]
[ContentType("CSharp")]
[ContentType("Basic")]
[method: ImportingConstructor]
internal sealed class TranslatedQuickInfoSourceProvider(IViewElementFactoryService viewElements) : IAsyncQuickInfoSourceProvider
{
    public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer) =>
        new TranslatedQuickInfoSource(textBuffer, viewElements);
}

/// <summary>
/// Adds the translated documentation to the tooltip Visual Studio shows on hover, below the
/// original. Whatever goes wrong, the original tooltip is unaffected: this source answers with
/// nothing rather than failing.
/// <para>
/// A translation already known arrives within moments and is shown with the tooltip. One that takes
/// longer, because the model is translating it now, does not hold the tooltip up: the tooltip opens with
/// the section saying the translation is on its way, and the section is filled in, while the tooltip is
/// still open, as soon as the translation arrives. The user never has to move away and hover again.
/// </para>
/// </summary>
internal sealed class TranslatedQuickInfoSource(ITextBuffer buffer, IViewElementFactoryService viewElements) : IAsyncQuickInfoSource
{
    private const string Source = nameof(TranslatedQuickInfoSource);

    // How long a hover may be held up waiting for a translation. Longer than a cached answer
    // takes, shorter than anyone would wait for a tooltip.
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(800);

    public async Task<QuickInfoItem?> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
    {
        try
        {
            SnapshotPoint? triggerPoint = session.GetTriggerPoint(buffer.CurrentSnapshot);
            if (triggerPoint is null)
            {
                return null;
            }

            ITextSnapshot snapshot = triggerPoint.Value.Snapshot;

            SymbolDocumentation? documentation = await SymbolDocumentationReader
                .ReadAsync(snapshot, triggerPoint.Value.Position, cancellationToken)
                .ConfigureAwait(false);
            if (documentation is null)
            {
                return null;
            }

            Task<DocumentModel?> work = DocumentationTranslator.Shared.TranslateAsync(documentation.Xml);
            ITrackingSpan applicableTo = snapshot.CreateTrackingSpan(
                documentation.SpanStart, documentation.SpanLength, SpanTrackingMode.EdgeInclusive);

            if (await work.CompletesWithinAsync(Budget, cancellationToken).ConfigureAwait(false))
            {
#pragma warning disable VSTHRD003 // Started by DocumentationTranslator off the UI thread, and already complete here.
                DocumentModel? translated = await work.ConfigureAwait(false);
#pragma warning restore VSTHRD003

                ContainerElement? content = translated is null
                    ? null
                    : await CreateContentAsync(translated, snapshot, cancellationToken).ConfigureAwait(false);

                return content is null ? null : new QuickInfoItem(applicableTo, content);
            }

            // WPF elements belong to the thread that made them, and the tooltip is drawn on the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            PendingTranslation pending = new(viewElements, session.TextView);
            _ = FillInWhenReadyAsync(pending, work, snapshot, session);

            return new QuickInfoItem(applicableTo, pending.Element);
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

    // Runs after the tooltip has been returned, so nothing may escape from it.
    private static async Task FillInWhenReadyAsync(
        PendingTranslation pending, Task<DocumentModel?> work, ITextSnapshot snapshot, IAsyncQuickInfoSession session)
    {
        try
        {
#pragma warning disable VSTHRD003 // Started by DocumentationTranslator off the UI thread; awaiting it here cannot deadlock.
            DocumentModel? translated = await work.ConfigureAwait(false);
#pragma warning restore VSTHRD003

            ContainerElement? content = translated is null
                ? null
                : await CreateContentAsync(translated, snapshot, CancellationToken.None).ConfigureAwait(false);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // A tooltip that has closed in the meantime is left alone; the translation is in the Engine's cache,
            // so the next hover shows it at once.
            if (session.State != QuickInfoSessionState.Dismissed)
            {
                pending.Show(content);
            }
        }
        catch (Exception ex)
        {
            ActivityLog.LogError(Source, $"Filling in a translation failed: {ex}");
        }
    }

    private static async Task<ContainerElement?> CreateContentAsync(
        DocumentModel translated, ITextSnapshot snapshot, CancellationToken cancellationToken)
    {
        // The documentation ids carry less than the author wrote (a generic's type parameters
        // are gone), so the compilation is asked what each reference really is.
        IReadOnlyList<string> targets = DocumentRenderer.CollectReferenceTargets(translated.Summary, translated.Remarks);
        IReadOnlyDictionary<string, ResolvedReference> references = await SymbolReferenceResolver
            .ResolveAsync(snapshot, targets, cancellationToken)
            .ConfigureAwait(false);

        return DocumentationElements.Create(translated, references);
    }
}