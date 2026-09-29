namespace VisualStudioTranslator.Core.Documentation;

/// <summary>
/// One translation unit extracted from a <see cref="DocumentModel"/>: the full set of
/// inlines making up a single paragraph, or a single list item's term or description.
/// <see cref="Id"/> is only meaningful together with the exact <see cref="DocumentModel"/>
/// it was produced from - it is a position in a deterministic walk, not a stable key
/// across different documents or across edits to the same one.
/// </summary>
public sealed record Segment
{
    public required int Id { get; init; }

    public required IReadOnlyList<Inline> Inlines { get; init; }
}