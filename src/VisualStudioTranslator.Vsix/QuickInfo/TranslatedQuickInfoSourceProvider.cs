using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Vsix.Symbols;
using VisualStudioTranslator.Vsix.Translation;

namespace VisualStudioTranslator.Vsix.QuickInfo;

[Export(typeof(IAsyncQuickInfoSourceProvider))]
[Name("Visual Studio Translator Quick Info")]
[ContentType("CSharp")]
[ContentType("Basic")]
internal sealed class TranslatedQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
{
    public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer) =>
        new TranslatedQuickInfoSource(textBuffer);
}

/// <summary>
/// Adds the translated documentation to the tooltip Visual Studio shows on hover, below the
/// original. Whatever goes wrong, the original tooltip is unaffected: this source answers with
/// nothing rather than failing.
/// </summary>
internal sealed class TranslatedQuickInfoSource(ITextBuffer buffer) : IAsyncQuickInfoSource
{
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

            DocumentModel? translated = await DocumentationTranslator.Shared
                .TryTranslateAsync(documentation.Xml, Budget, cancellationToken)
                .ConfigureAwait(false);
            if (translated is null)
            {
                return null;
            }

            // The documentation ids carry less than the author wrote (a generic's type parameters
            // are gone), so the compilation is asked what each reference really is.
            IReadOnlyList<string> targets = DocumentRenderer.CollectReferenceTargets(translated.Summary, translated.Remarks);
            IReadOnlyDictionary<string, ResolvedReference> references = await SymbolReferenceResolver
                .ResolveAsync(snapshot, targets, cancellationToken)
                .ConfigureAwait(false);

            ContainerElement? content = DocumentationElements.Create(translated, references);
            if (content is null)
            {
                return null;
            }

            ITrackingSpan applicableTo = snapshot.CreateTrackingSpan(
                documentation.SpanStart, documentation.SpanLength, SpanTrackingMode.EdgeInclusive);

            return new QuickInfoItem(applicableTo, content);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ActivityLog.LogError(nameof(TranslatedQuickInfoSource), $"Quick Info failed: {ex}");
            return null;
        }
    }

    public void Dispose()
    {
    }
}