using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Classification;
using System.Collections.Generic;
using System.Linq;
using VisualStudioTranslator.Core.Documentation;

namespace VisualStudioTranslator.Vsix.QuickInfo;

/// <summary>Turns a translated document into the elements Quick Info knows how to draw.</summary>
internal static class DocumentationElements
{
    /// <returns>The summary as stacked text, or <see langword="null"/> when there is no summary to show.</returns>
    public static ContainerElement? CreateSummary(DocumentModel document)
    {
        IReadOnlyList<IReadOnlyList<RenderedRun>> paragraphs = DocumentRenderer.RenderParagraphs(document.Summary);
        if (paragraphs.Count == 0)
        {
            return null;
        }

        List<object> elements = new List<object>
        {
            // A muted label so it is clear which part of the tooltip is the translation.
            new ClassifiedTextElement(new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment, "Translation")),
        };

        foreach (IReadOnlyList<RenderedRun> paragraph in paragraphs)
        {
            elements.Add(new ClassifiedTextElement(paragraph.Select(ToRun)));
        }

        return new ContainerElement(ContainerElementStyle.Stacked, elements);
    }

    // Emphasis has no counterpart among the run styles the tooltip offers, so it reads as plain text for now.
    private static ClassifiedTextRun ToRun(RenderedRun run)
    {
        switch (run.Style)
        {
            case RunStyle.Code:
                return new ClassifiedTextRun(PredefinedClassificationTypeNames.Identifier, run.Text, ClassifiedTextRunStyle.UseClassificationFont);

            case RunStyle.Keyword:
                return new ClassifiedTextRun(PredefinedClassificationTypeNames.Keyword, run.Text, ClassifiedTextRunStyle.UseClassificationFont);

            default:
                return new ClassifiedTextRun(PredefinedClassificationTypeNames.NaturalLanguage, run.Text);
        }
    }
}