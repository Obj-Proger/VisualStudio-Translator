using Microsoft.CodeAnalysis.Classification;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text.Adornments;
using VisualStudioTranslator.Core.Documentation;

namespace VisualStudioTranslator.Vsix.QuickInfo;

/// <summary>Turns a translated document into the elements Quick Info knows how to draw.</summary>
internal static class DocumentationElements
{
    /// <summary>
    /// The sections <see cref="Create"/> draws. Whatever else a comment holds is not shown, so it need not be translated:
    /// parameters are signature help's business, and the tooltip names exceptions without their descriptions.
    /// </summary>
    public static IReadOnlyList<string> ShownSections { get; } = ["summary", "remarks", "returns", "value"];

    /// <returns>
    /// The translated sections as stacked text, in the order the original tooltip has them, or
    /// <see langword="null"/> when there is none.
    /// </returns>
    public static ContainerElement? Create(DocumentModel document, IReadOnlyDictionary<string, ResolvedReference> references)
    {
        List<object> elements = [];

        // Remarks follow the summary with no caption, as in the original tooltip.
        AddParagraphs(elements, DocumentRenderer.RenderParagraphs(document.Summary, references));
        AddParagraphs(elements, DocumentRenderer.RenderParagraphs(document.Remarks, references));

        // The captions are in English for now, like every other word of the extension's own. They are translated
        // with the rest of its interface, not by the model: a caption is the product's text, not the author's.
        AddCaptioned(elements, "Returns:", DocumentRenderer.RenderParagraphs(document.Returns, references));
        AddCaptioned(elements, "Value:", DocumentRenderer.RenderParagraphs(document.Value, references));

        if (elements.Count == 0)
        {
            return null;
        }

        elements.Insert(0, Label());

        return new ContainerElement(ContainerElementStyle.Stacked | ContainerElementStyle.VerticalPadding, elements);
    }

    /// <summary>The sections whose references the tooltip has to resolve before drawing them.</summary>
    public static Section?[] SectionsOf(DocumentModel document) =>
        [document.Summary, document.Remarks, document.Returns, document.Value];

    /// <summary>The section of a tooltip whose translation is still on its way.</summary>
    public static ContainerElement Pending() => new(
        ContainerElementStyle.Stacked | ContainerElementStyle.VerticalPadding,
        Label(),

        // The grey of code the compiler leaves out: present, but not yet the thing itself.
        new ClassifiedTextElement(
            new ClassifiedTextRun(ClassificationTypeNames.ExcludedCode, "Translating\u2026", ClassifiedTextRunStyle.Italic)));

    private static void AddParagraphs(List<object> elements, IReadOnlyList<IReadOnlyList<RenderedRun>> paragraphs)
    {
        foreach (IReadOnlyList<RenderedRun> paragraph in paragraphs)
        {
            elements.Add(new ClassifiedTextElement(paragraph.Select(ToRun)));
        }
    }

    // A caption on its own line and the section's paragraphs under it, kept together, as the original tooltip
    // draws "Returns:".
    private static void AddCaptioned(List<object> elements, string caption, IReadOnlyList<IReadOnlyList<RenderedRun>> paragraphs)
    {
        if (paragraphs.Count == 0)
        {
            return;
        }

        List<object> section = [new ClassifiedTextElement(new ClassifiedTextRun(ClassificationTypeNames.Text, caption))];
        AddParagraphs(section, paragraphs);
        elements.Add(new ContainerElement(ContainerElementStyle.Stacked, section));
    }

    // The colour of control keywords ("if", "return"), so the label is clearly not part of the original documentation.
    private static ClassifiedTextElement Label() =>
        new(new ClassifiedTextRun(ClassificationTypeNames.ControlKeyword, "Translation"));

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