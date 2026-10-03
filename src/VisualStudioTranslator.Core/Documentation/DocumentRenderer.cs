namespace VisualStudioTranslator.Core.Documentation;

/// <summary>How a run of text should look. Deliberately coarser than any real presentation layer.</summary>
public enum RunStyle
{
    Text,
    Code,
    Keyword,
    Emphasis,

    /// <summary>A reference to a symbol; <see cref="RenderedRun.Reference"/> says what kind.</summary>
    Reference,
}

/// <summary>What a reference points at, so the presentation layer can colour it as the editor would.</summary>
public enum ReferenceKind
{
    Unknown,
    Class,
    Struct,
    Interface,
    Enum,
    Delegate,
    Namespace,
    Method,
    Property,
    Field,
    Constant,
    EnumMember,
    Event,
    TypeParameter,
    Parameter,
}

/// <summary>A reference target resolved to the text to show for it and the kind of symbol it is.</summary>
public sealed record ResolvedReference(string DisplayText, ReferenceKind Kind);

public sealed record RenderedRun(string Text, RunStyle Style, ReferenceKind? Reference = null);

/// <summary>
/// Flattens a documentation section into paragraphs of styled runs: everything a tooltip needs,
/// with nothing tied to the editor. It lives here, rather than next to the code that draws the
/// tooltip, so it can be tested without Visual Studio, and any other surface that shows
/// documentation can reuse it.
/// <para>
/// A documentation id carries less than the author wrote: "Result{TValue}" arrives as
/// "T:Ns.Result`1", with the name of the type parameter gone. Showing it properly needs the
/// compilation, which only the caller has, so the caller can pass in what it resolved. Without
/// that the renderer falls back to a short name guessed from the id itself.
/// </para>
/// </summary>
public static class DocumentRenderer
{
    public static IReadOnlyList<IReadOnlyList<RenderedRun>> RenderParagraphs(
        Section? section, IReadOnlyDictionary<string, ResolvedReference>? references = null)
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
                    AddIfAny(paragraphs, Runs(paragraph.Inlines, references));
                    break;

                case ListBlock list:
                    AddList(paragraphs, list, references);
                    break;

                case CodeBlock code:
                    paragraphs.Add([new RenderedRun(code.Code, RunStyle.Code)]);
                    break;
            }
        }

        return paragraphs;
    }

    /// <summary>
    /// The targets of every <c>cref</c> reference in the sections, each once, in order of first
    /// appearance: the list a caller needs to resolve before rendering.
    /// </summary>
    public static IReadOnlyList<string> CollectReferenceTargets(params Section?[] sections)
    {
        List<string> targets = [];
        HashSet<string> seen = [];

        foreach (Section? section in sections)
        {
            if (section is null)
            {
                continue;
            }

            foreach (Block block in section.Blocks)
            {
                switch (block)
                {
                    case Paragraph paragraph:
                        Collect(paragraph.Inlines, targets, seen);
                        break;

                    case ListBlock list:
                        foreach (ListItem item in list.Items)
                        {
                            Collect(item.Term, targets, seen);
                            Collect(item.Description, targets, seen);
                        }

                        break;
                }
            }
        }

        return targets;
    }

    private static void Collect(IEnumerable<Inline> inlines, List<string> targets, HashSet<string> seen)
    {
        foreach (Inline inline in inlines)
        {
            switch (inline)
            {
                case EmphasisRun emphasis:
                    Collect(emphasis.Content, targets, seen);
                    break;

                case Ref reference:
                    if (reference.Kind == RefKind.Cref && seen.Add(reference.Target))
                    {
                        targets.Add(reference.Target);
                    }

                    if (reference.DisplayContent is not null)
                    {
                        Collect(reference.DisplayContent, targets, seen);
                    }

                    break;
            }
        }
    }

    private static void AddList(
        List<IReadOnlyList<RenderedRun>> paragraphs, ListBlock list, IReadOnlyDictionary<string, ResolvedReference>? references)
    {
        for (int i = 0; i < list.Items.Count; i++)
        {
            ListItem item = list.Items[i];
            string marker = list.Kind == ListKind.Number ? $"{i + 1}. " : "• ";

            List<RenderedRun> runs = [new RenderedRun(marker, RunStyle.Text)];

            if (item.Term.Count > 0)
            {
                runs.AddRange(Runs(item.Term, references));

                if (item.Description.Count > 0)
                {
                    runs.Add(new RenderedRun(" — ", RunStyle.Text));
                }
            }

            runs.AddRange(Runs(item.Description, references));
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

    private static List<RenderedRun> Runs(IEnumerable<Inline> inlines, IReadOnlyDictionary<string, ResolvedReference>? references)
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
                    // Only plain text becomes emphasized; code and references inside it stay as they are.
                    foreach (RenderedRun run in Runs(emphasis.Content, references))
                    {
                        runs.Add(run.Style == RunStyle.Text ? run with { Style = RunStyle.Emphasis } : run);
                    }

                    break;

                case Ref reference:
                    runs.AddRange(RefRuns(reference, references));
                    break;
            }
        }

        return runs;
    }

    private static IEnumerable<RenderedRun> RefRuns(Ref reference, IReadOnlyDictionary<string, ResolvedReference>? references)
    {
        switch (reference.Kind)
        {
            case RefKind.Langword:
                return [new RenderedRun(reference.Target, RunStyle.Keyword)];

            case RefKind.Paramref:
                return [new RenderedRun(reference.Target, RunStyle.Reference, ReferenceKind.Parameter)];

            case RefKind.Typeparamref:
                return [new RenderedRun(reference.Target, RunStyle.Reference, ReferenceKind.TypeParameter)];

            default:
                // A reference that carries its own text shows that text.
                if (reference.DisplayContent is { Count: > 0 } display)
                {
                    return Runs(display, references);
                }

                if (references is not null && references.TryGetValue(reference.Target, out ResolvedReference resolved))
                {
                    return [new RenderedRun(resolved.DisplayText, RunStyle.Reference, resolved.Kind)];
                }

                return [new RenderedRun(DisplayName(reference.Target), RunStyle.Reference, ReferenceKind.Unknown)];
        }
    }

    // The fallback when nothing resolved the id: "T:System.String" -> "String",
    // "M:Foo.Bar(System.Int32)" -> "Bar", "T:System.Collections.Generic.List`1" -> "List",
    // "M:Foo.Bar.#ctor" -> "Bar". The type parameters of a generic are lost here; that is the
    // reason callers should resolve references when they can.
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