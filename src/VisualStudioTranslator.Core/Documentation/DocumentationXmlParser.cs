using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace VisualStudioTranslator.Core.Documentation;

/// <summary>
/// Parses the raw XML produced by <c>ISymbol.GetDocumentationCommentXml()</c> into a
/// <see cref="DocumentModel"/>. Works for both C# and VB documentation comments, since
/// both compile down to the same XML schema; nothing here is C#-specific.
/// </summary>
public static class DocumentationXmlParser
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Parses <paramref name="xml"/> into a <see cref="DocumentModel"/>. Returns an empty
    /// model - never throws - for a <see langword="null"/>, empty, or malformed input, so
    /// that one symbol with an unparseable comment cannot take down translation for
    /// everything else being hovered over.
    /// </summary>
    public static DocumentModel Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return new DocumentModel();
        }

        XElement member;
        try
        {
            member = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return new DocumentModel();
        }

        return new DocumentModel
        {
            Summary = ParseSectionOrNull(member.Element("summary")),
            Remarks = ParseSectionOrNull(member.Element("remarks")),
            Returns = ParseSectionOrNull(member.Element("returns")),
            Value = ParseSectionOrNull(member.Element("value")),
            Params = ParseNamedSections(member.Elements("param"), e => e.Attribute("name")?.Value),
            TypeParams = ParseNamedSections(member.Elements("typeparam"), e => e.Attribute("name")?.Value),
            Exceptions = ParseNamedSections(member.Elements("exception"), e => e.Attribute("cref")?.Value),
            Examples = [.. member.Elements("example").Select(ParseSectionOrNull).OfType<Section>()],
            SeeAlso = [.. member.Elements("seealso")
                .Select(e => e.Attribute("cref")?.Value)
                .Where(cref => !string.IsNullOrEmpty(cref))
                .OfType<string>()],
        };
    }

    private static IReadOnlyList<NamedSection> ParseNamedSections(
        IEnumerable<XElement> elements, Func<XElement, string?> nameSelector)
    {
        List<NamedSection> result = [];

        foreach (XElement element in elements)
        {
            string? name = nameSelector(element);
            if (name is not { Length: > 0 })
            {
                continue;
            }

            result.Add(new NamedSection { Name = name, Content = ParseSection(element) });
        }

        return result;
    }

    /// <summary>
    /// Absent element and present-but-empty element (for example a bare
    /// <c>&lt;summary&gt;&lt;/summary&gt;</c>, or one containing only
    /// <c>&lt;inheritdoc/&gt;</c>) are treated the same: both mean "nothing to show
    /// here", so both collapse to <see langword="null"/> rather than an empty
    /// <see cref="Section"/>.
    /// </summary>
    private static Section? ParseSectionOrNull(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        Section section = ParseSection(element);
        return section.Blocks.Count > 0 ? section : null;
    }

    private static Section ParseSection(XElement element)
    {
        List<Block> blocks = [];
        List<Inline> currentParagraph = [];

        void FlushParagraph()
        {
            // Trim first, then check what's left - a whitespace-only text node (from
            // indentation in the source XML) collapses to a single space, which is a
            // non-empty list *before* trimming even though nothing meaningful remains
            // after. Checking Count on the untrimmed list here would add a spurious
            // empty Paragraph for every run of formatting whitespace between block-level
            // elements or at the end of a section.
            IReadOnlyList<Inline> trimmed = TrimEdges(currentParagraph);
            if (trimmed.Count > 0)
            {
                blocks.Add(new Paragraph { Inlines = trimmed });
            }
            currentParagraph = [];
        }

        foreach (XNode node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    AppendText(currentParagraph, text.Value);
                    break;

                case XElement { Name.LocalName: "para" } para:
                    FlushParagraph();
                    IReadOnlyList<Inline> paraInlines = ParseInlines(para.Nodes());
                    if (paraInlines.Count > 0)
                    {
                        blocks.Add(new Paragraph { Inlines = paraInlines });
                    }
                    break;

                case XElement { Name.LocalName: "list" } list:
                    FlushParagraph();
                    blocks.Add(ParseList(list));
                    break;

                case XElement { Name.LocalName: "code" } code:
                    FlushParagraph();
                    blocks.Add(ParseCode(code));
                    break;

                case XElement inlineElement:
                    AppendInlineElement(currentParagraph, inlineElement);
                    break;
            }
        }

        FlushParagraph();

        return new Section { Blocks = blocks };
    }

    private static IReadOnlyList<Inline> ParseInlines(IEnumerable<XNode> nodes)
    {
        List<Inline> inlines = [];

        foreach (XNode node in nodes)
        {
            switch (node)
            {
                case XText text:
                    AppendText(inlines, text.Value);
                    break;

                case XElement element:
                    AppendInlineElement(inlines, element);
                    break;
            }
        }

        return TrimEdges(inlines);
    }

    private static void AppendText(List<Inline> inlines, string raw)
    {
        string normalized = WhitespaceRun.Replace(raw, " ");
        if (normalized.Length > 0)
        {
            inlines.Add(new TextRun { Text = normalized });
        }
    }

    private static void AppendInlineElement(List<Inline> inlines, XElement element)
    {
        switch (element.Name.LocalName)
        {
            case "c":
                inlines.Add(new CodeSpan { Text = element.Value });
                break;

            case "see":
            case "seealso":
                inlines.Add(ParseRef(element));
                break;

            case "paramref":
                inlines.Add(new Ref { Kind = RefKind.Paramref, Target = element.Attribute("name")?.Value ?? string.Empty });
                break;

            case "typeparamref":
                inlines.Add(new Ref { Kind = RefKind.Typeparamref, Target = element.Attribute("name")?.Value ?? string.Empty });
                break;

            case "b":
            case "i":
                inlines.Add(new EmphasisRun { Content = ParseInlines(element.Nodes()) });
                break;

            default:
                // Covers both genuinely unknown elements and self-closing ones such as
                // <inheritdoc/>: recursing into an element's own children is a no-op
                // when it has none, so this degrades safely instead of dropping or
                // mishandling content we did not anticipate.
                inlines.AddRange(ParseInlines(element.Nodes()));
                break;
        }
    }

    private static Ref ParseRef(XElement element)
    {
        string? langword = element.Attribute("langword")?.Value;
        if (langword is { Length: > 0 })
        {
            return new Ref { Kind = RefKind.Langword, Target = langword };
        }

        string cref = element.Attribute("cref")?.Value ?? string.Empty;
        IReadOnlyList<Inline> display = ParseInlines(element.Nodes());

        return new Ref
        {
            Kind = RefKind.Cref,
            Target = cref,
            DisplayContent = display.Count > 0 ? display : null,
        };
    }

    private static ListBlock ParseList(XElement element)
    {
        ListKind kind = element.Attribute("type")?.Value switch
        {
            "number" => ListKind.Number,
            "table" => ListKind.Table,
            _ => ListKind.Bullet,
        };

        List<ListItem> items = [];

        // <listheader> defines column headings for a table, not a data row - it is
        // deliberately not represented in the model, rather than forced into a
        // ListItem it does not semantically fit.
        foreach (XElement item in element.Elements("item"))
        {
            XElement? term = item.Element("term");
            XElement? description = item.Element("description");

            items.Add(new ListItem
            {
                Term = term is not null ? ParseInlines(term.Nodes()) : [],
                Description = description is not null ? ParseInlines(description.Nodes()) : [],
            });
        }

        return new ListBlock { Kind = kind, Items = items };
    }

    private static CodeBlock ParseCode(XElement element)
    {
        string? language = element.Attribute("lang")?.Value ?? element.Attribute("language")?.Value;

        return new CodeBlock { Code = DedentCode(element.Value), Language = language };
    }

    /// <summary>
    /// Strips blank lines from both ends and the leading whitespace shared by every
    /// remaining line, matching how the block was indented in the source file rather
    /// than however deep the enclosing member happens to sit.
    /// </summary>
    private static string DedentCode(string raw)
    {
        string[] lines = raw.Replace("\r\n", "\n").Split('\n');

        int start = 0;
        while (start < lines.Length && IsBlank(lines[start]))
        {
            start++;
        }

        int end = lines.Length - 1;
        while (end >= start && IsBlank(lines[end]))
        {
            end--;
        }

        if (start > end)
        {
            return string.Empty;
        }

        int commonIndent = int.MaxValue;
        for (int i = start; i <= end; i++)
        {
            if (IsBlank(lines[i]))
            {
                continue;
            }

            int indent = lines[i].Length - lines[i].TrimStart(' ').Length;
            commonIndent = Math.Min(commonIndent, indent);
        }

        if (commonIndent == int.MaxValue)
        {
            commonIndent = 0;
        }

        IEnumerable<string> dedented = Enumerable.Range(start, end - start + 1)
            .Select(i => IsBlank(lines[i]) ? string.Empty : lines[i][Math.Min(commonIndent, lines[i].Length)..]);

        return string.Join("\n", dedented);
    }

    private static bool IsBlank(string line) => line.Trim().Length == 0;

    private static IReadOnlyList<Inline> TrimEdges(List<Inline> inlines)
    {
        TrimStart(inlines);
        TrimEnd(inlines);
        return inlines;
    }

    private static void TrimStart(List<Inline> inlines)
    {
        while (inlines.Count > 0 && inlines[0] is TextRun run)
        {
            string trimmed = run.Text.TrimStart(' ');
            if (trimmed.Length == 0)
            {
                inlines.RemoveAt(0);
                continue;
            }

            if (trimmed != run.Text)
            {
                inlines[0] = run with { Text = trimmed };
            }

            break;
        }
    }

    private static void TrimEnd(List<Inline> inlines)
    {
        while (inlines.Count > 0 && inlines[^1] is TextRun run)
        {
            string trimmed = run.Text.TrimEnd(' ');
            if (trimmed.Length == 0)
            {
                inlines.RemoveAt(inlines.Count - 1);
                continue;
            }

            if (trimmed != run.Text)
            {
                inlines[^1] = run with { Text = trimmed };
            }

            break;
        }
    }
}