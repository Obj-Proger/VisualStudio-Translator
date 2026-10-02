using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace VisualStudioTranslator.Core.Documentation;

/// <summary>
/// Turns a <see cref="DocumentModel"/> back into documentation XML that
/// <see cref="DocumentationXmlParser"/> reads without loss: parsing the output gives the model
/// back, for anything the parser itself can produce. It exists so a translated document can
/// cross the process boundary as a plain string; the model's inline and block types are
/// abstract, and sending those over the wire would need serializer support for polymorphism
/// that a string does not.
/// <para>
/// Only the content is written. The attributes of the original root element (the member's
/// name) are not part of the model and are not kept. Emphasis is always written as
/// <c>&lt;i&gt;</c>, since the model does not remember whether the author used <c>&lt;b&gt;</c>,
/// and the two render the same.
/// </para>
/// <para>
/// The output is deliberately compact. Whitespace between elements would be read back as
/// real spaces, so none is ever added. Like the parser, this never throws: characters that
/// cannot appear in XML are dropped rather than allowed to fail the whole document.
/// </para>
/// </summary>
public static class DocumentationXmlWriter
{
    public static string Write(DocumentModel model)
    {
        XElement member = new("member");

        AddSection(member, "summary", model.Summary);
        AddSection(member, "remarks", model.Remarks);
        AddSection(member, "returns", model.Returns);
        AddSection(member, "value", model.Value);

        AddNamedSections(member, "param", "name", model.Params);
        AddNamedSections(member, "typeparam", "name", model.TypeParams);
        AddNamedSections(member, "exception", "cref", model.Exceptions);

        foreach (Section example in model.Examples)
        {
            member.Add(SectionElement("example", example));
        }

        foreach (string target in model.SeeAlso)
        {
            member.Add(new XElement("seealso", new XAttribute("cref", Clean(target))));
        }

        return member.ToString(SaveOptions.DisableFormatting);
    }

    private static void AddSection(XElement parent, string name, Section? section)
    {
        if (section is not null)
        {
            parent.Add(SectionElement(name, section));
        }
    }

    private static void AddNamedSections(
        XElement parent, string elementName, string attributeName, IReadOnlyList<NamedSection> sections)
    {
        foreach (NamedSection named in sections)
        {
            XElement element = SectionElement(elementName, named.Content);
            element.Add(new XAttribute(attributeName, Clean(named.Name)));
            parent.Add(element);
        }
    }

    private static XElement SectionElement(string name, Section section) =>
        new(name, section.Blocks.Select(BlockElement).OfType<XElement>());

    private static XElement? BlockElement(Block block) => block switch
    {
        Paragraph paragraph => new XElement("para", InlineNodes(paragraph.Inlines)),
        ListBlock list => ListElement(list),
        CodeBlock code => CodeElement(code),
        _ => null,
    };

    private static XElement ListElement(ListBlock list)
    {
        string type = list.Kind switch
        {
            ListKind.Number => "number",
            ListKind.Table => "table",
            _ => "bullet",
        };

        XElement element = new("list", new XAttribute("type", type));

        foreach (ListItem item in list.Items)
        {
            XElement itemElement = new("item");

            // An empty part is left out: the parser reads a missing term or description
            // as an empty list, exactly what an empty one would have produced.
            if (item.Term.Count > 0)
            {
                itemElement.Add(new XElement("term", InlineNodes(item.Term)));
            }

            if (item.Description.Count > 0)
            {
                itemElement.Add(new XElement("description", InlineNodes(item.Description)));
            }

            element.Add(itemElement);
        }

        return element;
    }

    private static XElement CodeElement(CodeBlock code)
    {
        XElement element = new("code", Clean(code.Code));

        if (code.Language is not null)
        {
            element.Add(new XAttribute("lang", Clean(code.Language)));
        }

        return element;
    }

    private static List<XNode> InlineNodes(IEnumerable<Inline> inlines)
    {
        List<XNode> nodes = [];

        foreach (Inline inline in inlines)
        {
            switch (inline)
            {
                case TextRun text:
                    nodes.Add(new XText(Clean(text.Text)));
                    break;

                case CodeSpan code:
                    nodes.Add(new XElement("c", Clean(code.Text)));
                    break;

                case EmphasisRun emphasis:
                    nodes.Add(new XElement("i", InlineNodes(emphasis.Content)));
                    break;

                case Ref reference:
                    nodes.Add(RefElement(reference));
                    break;
            }
        }

        return nodes;
    }

    private static XElement RefElement(Ref reference)
    {
        switch (reference.Kind)
        {
            case RefKind.Langword:
                return new XElement("see", new XAttribute("langword", Clean(reference.Target)));

            case RefKind.Paramref:
                return new XElement("paramref", new XAttribute("name", Clean(reference.Target)));

            case RefKind.Typeparamref:
                return new XElement("typeparamref", new XAttribute("name", Clean(reference.Target)));

            default:
                XElement element = new("see", new XAttribute("cref", Clean(reference.Target)));

                if (reference.DisplayContent is not null)
                {
                    element.Add(InlineNodes(reference.DisplayContent));
                }

                return element;
        }
    }

    // Drops what XML cannot represent: control characters other than tab and line breaks,
    // and surrogate halves that are not part of a pair. Valid pairs (emoji, rare CJK) pass through.
    private static string Clean(string text)
    {
        StringBuilder clean = new(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                clean.Append(c).Append(text[i + 1]);
                i++;
            }
            else if (XmlConvert.IsXmlChar(c))
            {
                clean.Append(c);
            }
        }

        return clean.ToString();
    }
}