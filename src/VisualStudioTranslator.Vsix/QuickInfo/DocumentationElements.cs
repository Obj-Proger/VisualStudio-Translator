using Microsoft.CodeAnalysis.Classification;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text.Adornments;
using System.Collections.Generic;
using System.Linq;
using VisualStudioTranslator.Core.Documentation;

namespace VisualStudioTranslator.Vsix.QuickInfo;

/// <summary>Turns a translated document into the elements Quick Info knows how to draw.</summary>
internal static class DocumentationElements
{
    /// <returns>The summary and the remarks as stacked text, or <see langword="null"/> when there is neither.</returns>
    public static ContainerElement? Create(DocumentModel document, IReadOnlyDictionary<string, ResolvedReference> references)
    {
        // Remarks follow the summary, as in the original tooltip. The sections that carry a caption
        // of their own ("Returns:", "Exceptions:") wait until those captions exist in the target language.
        List<IReadOnlyList<RenderedRun>> paragraphs = [];
        paragraphs.AddRange(DocumentRenderer.RenderParagraphs(document.Summary, references));
        paragraphs.AddRange(DocumentRenderer.RenderParagraphs(document.Remarks, references));

        if (paragraphs.Count == 0)
        {
            return null;
        }

        List<object> elements =
        [
            // The colour of control keywords ("if", "return"), so the label is clearly not
            // part of the original documentation.
            new ClassifiedTextElement(new ClassifiedTextRun(ClassificationTypeNames.ControlKeyword, "Translation")),
        ];

        foreach (IReadOnlyList<RenderedRun> paragraph in paragraphs)
        {
            elements.Add(new ClassifiedTextElement(paragraph.Select(ToRun)));
        }

        return new ContainerElement(ContainerElementStyle.Stacked | ContainerElementStyle.VerticalPadding, elements);
    }

    // Emphasis has no counterpart among the run styles the tooltip offers, so it reads as plain text for now.
    private static ClassifiedTextRun ToRun(RenderedRun run) => run.Style switch
    {
        RunStyle.Code => new ClassifiedTextRun(
            PredefinedClassificationTypeNames.Identifier, run.Text, ClassifiedTextRunStyle.UseClassificationFont),
        RunStyle.Keyword => new ClassifiedTextRun(ClassificationTypeNames.Keyword, run.Text),
        RunStyle.Reference => new ClassifiedTextRun(ClassificationFor(run.Reference), run.Text),
        _ => new ClassifiedTextRun(PredefinedClassificationTypeNames.NaturalLanguage, run.Text),
    };

    // The names the editor itself colours these symbols by, so a reference in a tooltip matches the code.
    private static string ClassificationFor(ReferenceKind? kind) => kind switch
    {
        ReferenceKind.Class => ClassificationTypeNames.ClassName,
        ReferenceKind.Struct => ClassificationTypeNames.StructName,
        ReferenceKind.Interface => ClassificationTypeNames.InterfaceName,
        ReferenceKind.Enum => ClassificationTypeNames.EnumName,
        ReferenceKind.Delegate => ClassificationTypeNames.DelegateName,
        ReferenceKind.Namespace => ClassificationTypeNames.NamespaceName,
        ReferenceKind.Method => ClassificationTypeNames.MethodName,
        ReferenceKind.Property => ClassificationTypeNames.PropertyName,
        ReferenceKind.Field => ClassificationTypeNames.FieldName,
        ReferenceKind.Constant => ClassificationTypeNames.ConstantName,
        ReferenceKind.EnumMember => ClassificationTypeNames.EnumMemberName,
        ReferenceKind.Event => ClassificationTypeNames.EventName,
        ReferenceKind.TypeParameter => ClassificationTypeNames.TypeParameterName,
        ReferenceKind.Parameter => ClassificationTypeNames.ParameterName,
        _ => ClassificationTypeNames.Identifier,
    };
}