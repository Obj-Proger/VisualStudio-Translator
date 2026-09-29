namespace VisualStudioTranslator.Core.Documentation;

/// <summary>
/// Extracts translation units from a <see cref="DocumentModel"/> and rebuilds a model
/// with translated replacements substituted back in. Segmenting and composing share a
/// single tree walk (<see cref="Walk"/>) instead of being two independently written
/// traversals, so segment IDs can never drift out of sync between the two directions -
/// an easy mistake to make if a future field added to <see cref="DocumentModel"/> were
/// visited in one hand-written walk but forgotten in the other.
/// </summary>
public static class DocumentSegmenter
{
    /// <summary>Extracts every translation unit from <paramref name="model"/>, in a fixed, deterministic order.</summary>
    public static IReadOnlyList<Segment> Segment(DocumentModel model)
    {
        List<Segment> segments = [];
        int nextId = 0;

        Walk(model, ref nextId, (id, inlines) =>
        {
            segments.Add(new Segment { Id = id, Inlines = inlines });
            return inlines;
        });

        return segments;
    }

    /// <summary>
    /// Rebuilds <paramref name="original"/> with each segment whose id appears in
    /// <paramref name="translations"/> replaced by its translated inlines. A segment
    /// with no entry in <paramref name="translations"/> - for example because its
    /// translation failed validation - is left exactly as it was in the original, so a
    /// single failed segment degrades to showing the original text there rather than
    /// dropping content or failing the whole document.
    /// </summary>
    public static DocumentModel Compose(DocumentModel original, IReadOnlyDictionary<int, IReadOnlyList<Inline>> translations)
    {
        int nextId = 0;

        return Walk(original, ref nextId, (id, inlines) =>
            translations.TryGetValue(id, out IReadOnlyList<Inline>? translated) ? translated : inlines);
    }

    /// <summary>
    /// Walks every segment-bearing position in <paramref name="model"/>, in declaration
    /// order (summary, remarks, returns, value, params, type params, exceptions,
    /// examples; within a section, blocks in order; within a list, items in order, term
    /// before description). <paramref name="visit"/> is called once per non-empty
    /// segment with the next sequential id and that segment's current inlines, and its
    /// return value becomes the inlines used in the rebuilt model - the identity
    /// function for segmenting, a translation lookup for composing.
    /// </summary>
    private static DocumentModel Walk(
        DocumentModel model, ref int nextId, Func<int, IReadOnlyList<Inline>, IReadOnlyList<Inline>> visit)
    {
        List<Section> examples = new(model.Examples.Count);
        foreach (Section example in model.Examples)
        {
            examples.Add(WalkSection(example, ref nextId, visit));
        }

        return model with
        {
            Summary = WalkSectionOrNull(model.Summary, ref nextId, visit),
            Remarks = WalkSectionOrNull(model.Remarks, ref nextId, visit),
            Returns = WalkSectionOrNull(model.Returns, ref nextId, visit),
            Value = WalkSectionOrNull(model.Value, ref nextId, visit),
            Params = WalkNamedSections(model.Params, ref nextId, visit),
            TypeParams = WalkNamedSections(model.TypeParams, ref nextId, visit),
            Exceptions = WalkNamedSections(model.Exceptions, ref nextId, visit),
            Examples = examples,
            // SeeAlso holds cref targets, not prose - never a translation unit, carried
            // through unchanged by the `model with { ... }` above.
        };
    }

    private static IReadOnlyList<NamedSection> WalkNamedSections(
        IReadOnlyList<NamedSection> sections, ref int nextId, Func<int, IReadOnlyList<Inline>, IReadOnlyList<Inline>> visit)
    {
        // A ref parameter cannot be captured by a lambda, so this is an explicit loop
        // rather than a LINQ Select - here and in the two walkers below.
        List<NamedSection> result = new(sections.Count);
        foreach (NamedSection section in sections)
        {
            result.Add(section with { Content = WalkSection(section.Content, ref nextId, visit) });
        }
        return result;
    }

    private static Section? WalkSectionOrNull(
        Section? section, ref int nextId, Func<int, IReadOnlyList<Inline>, IReadOnlyList<Inline>> visit) =>
        section is null ? null : WalkSection(section, ref nextId, visit);

    private static Section WalkSection(
        Section section, ref int nextId, Func<int, IReadOnlyList<Inline>, IReadOnlyList<Inline>> visit)
    {
        List<Block> blocks = new(section.Blocks.Count);
        foreach (Block block in section.Blocks)
        {
            blocks.Add(WalkBlock(block, ref nextId, visit));
        }
        return section with { Blocks = blocks };
    }

    private static Block WalkBlock(Block block, ref int nextId, Func<int, IReadOnlyList<Inline>, IReadOnlyList<Inline>> visit)
    {
        switch (block)
        {
            case Paragraph { Inlines.Count: > 0 } paragraph:
                return paragraph with { Inlines = visit(nextId++, paragraph.Inlines) };

            case ListBlock list:
                List<ListItem> items = new(list.Items.Count);
                foreach (ListItem item in list.Items)
                {
                    items.Add(WalkListItem(item, ref nextId, visit));
                }
                return list with { Items = items };

            // Paragraph with no inlines (defensive - the parser never produces one) and
            // CodeBlock (never a translation unit) both pass through unchanged.
            default:
                return block;
        }
    }

    private static ListItem WalkListItem(ListItem item, ref int nextId, Func<int, IReadOnlyList<Inline>, IReadOnlyList<Inline>> visit)
    {
        IReadOnlyList<Inline> term = item.Term.Count > 0 ? visit(nextId++, item.Term) : item.Term;
        IReadOnlyList<Inline> description = item.Description.Count > 0 ? visit(nextId++, item.Description) : item.Description;

        return item with { Term = term, Description = description };
    }
}