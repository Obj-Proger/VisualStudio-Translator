namespace VisualStudioTranslator.Core.Documentation;

/// <summary>How a run of text should look. Deliberately coarser than any real presentation layer.</summary>
public enum RunStyle
{
    Text,
    Code,
    Keyword,
    Emphasis,
}

public sealed record RenderedRun(string Text, RunStyle Style);

/// <summary>
/// Flattens a documentation section into paragraphs of styled runs: everything a tooltip needs,
/// with nothing tied to the editor. It lives here, rather than next to the code that draws the
/// tooltip, so it can be tested without Visual Studio, and any other surface that shows
/// documentation can reuse it.
/// </summary>
public static class DocumentRenderer
{
    public static IReadOnlyList<IReadOnlyList<RenderedRun>> RenderParagraphs(Section? section)
    {
        List<IReadOnlyList<RenderedRun>> paragraphs = [];

        if (section is null)
        {
            return paragraphs;
        }

        foreach (Block block in section.Blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    AddIfAny(paragraphs, Runs(paragraph.Inlines));
                    break;

                case ListBlock list:
                    AddList(paragraphs, list);
                    break;

                case CodeBlock code:
                    paragraphs.Add([new RenderedRun(code.Code, RunStyle.Code)]);
                    break;
            }
        }

        return paragraphs;
    }

    private static void AddList(List<IReadOnlyList<RenderedRun>> paragraphs, ListBlock list)
    {
        for (int i = 0; i < list.Items.Count; i++)
        {
            ListItem item = list.Items[i];
            string marker = list.Kind == ListKind.Number ? $"{i + 1}. " : "• ";

            List<RenderedRun> runs = [new RenderedRun(marker, RunStyle.Text)];

            if (item.Term.Count > 0)
            {
                runs.AddRange(Runs(item.Term));

                if (item.Description.Count > 0)
                {
                    runs.Add(new RenderedRun(" — ", RunStyle.Text));
                }
            }

            runs.AddRange(Runs(item.Description));
            paragraphs.Add(runs);
        }
    }

    private static void AddIfAny(List<IReadOnlyList<RenderedRun>> paragraphs, List<RenderedRun> runs)
    {
        if (runs.Count > 0)
        {
            paragraphs.Add(runs);
        }
    }

    private static List<RenderedRun> Runs(IEnumerable<Inline> inlines)
    {
        List<RenderedRun> runs = [];

        foreach (Inline inline in inlines)
        {
            switch (inline)
            {
                case TextRun text:
                    if (text.Text.Length > 0)
                    {
                        runs.Add(new RenderedRun(text.Text, RunStyle.Text));
                    }

                    break;

                case CodeSpan code:
                    runs.Add(new RenderedRun(code.Text, RunStyle.Code));
                    break;

                case EmphasisRun emphasis:
                    // Only plain text becomes emphasized; code inside it stays code.
                    foreach (RenderedRun run in Runs(emphasis.Content))
                    {
                        runs.Add(run.Style == RunStyle.Text ? run with { Style = RunStyle.Emphasis } : run);
                    }

                    break;

                case Ref reference:
                    runs.AddRange(RefRuns(reference));
                    break;
            }
        }

        return runs;
    }

    private static IEnumerable<RenderedRun> RefRuns(Ref reference)
    {
        switch (reference.Kind)
        {
            case RefKind.Langword:
                return [new RenderedRun(reference.Target, RunStyle.Keyword)];

            case RefKind.Paramref:
            case RefKind.Typeparamref:
                return [new RenderedRun(reference.Target, RunStyle.Code)];

            default:
                // A reference that carries its own text shows that text; otherwise the symbol's short name.
                if (reference.DisplayContent is { Count: > 0 } display)
                {
                    return Runs(display);
                }

                return [new RenderedRun(DisplayName(reference.Target), RunStyle.Code)];
        }
    }

    // "T:System.String" -> "String", "M:Foo.Bar(System.Int32)" -> "Bar",
    // "T:System.Collections.Generic.List`1" -> "List", "M:Foo.Bar.#ctor" -> "Bar".
    private static string DisplayName(string target)
    {
        string name = target;

        // A documentation id starts with a one-letter kind and a colon ("T:", "M:", "!:").
        if (name.Length > 2 && name[1] == ':')
        {
            name = name[2..];
        }

        int parenthesis = name.IndexOf('(');
        if (parenthesis >= 0)
        {
            name = name[..parenthesis];
        }

        string[] parts = name.Split('.');
        string last = parts[^1];

        if ((last == "#ctor" || last == "#cctor") && parts.Length > 1)
        {
            last = parts[^2];
        }

        // Generic arity markers: "List`1", "Method``2".
        int backtick = last.IndexOf('`');
        if (backtick >= 0)
        {
            last = last[..backtick];
        }

        return last.Length > 0 ? last : target;
    }
}