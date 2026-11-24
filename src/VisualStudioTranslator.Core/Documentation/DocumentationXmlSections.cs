using System.Xml;
using System.Xml.Linq;

namespace VisualStudioTranslator.Core.Documentation;

/// <summary>
/// Narrows a documentation comment, as <c>ISymbol.GetDocumentationCommentXml()</c> returns it, to the sections
/// a consumer is going to show. Translating a section nobody sees costs the same as one somebody does, and on
/// a local model that is seconds a segment: a method's parameters alone can triple the wait for its tooltip.
/// </summary>
public static class DocumentationXmlSections
{
    /// <summary>
    /// Returns <paramref name="xml"/> with only the top-level elements named in <paramref name="names"/>, in their
    /// original order and with their content untouched. Input that is not well-formed XML is returned as it is,
    /// for the parser to deal with.
    /// </summary>
    public static string Keep(string xml, IReadOnlyCollection<string> names)
    {
        XElement member;

        try
        {
            member = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return xml;
        }

        List<XElement> unwanted = [.. member.Elements().Where(element => !names.Contains(element.Name.LocalName))];

        if (unwanted.Count == 0)
        {
            return xml;
        }

        foreach (XElement element in unwanted)
        {
            element.Remove();
        }

        return XmlFragments.Write(member);
    }
}