using System.Xml;
using System.Xml.Linq;

namespace VisualStudioTranslator.Core.Documentation;

/// <summary>
/// The text side of <c>&lt;inheritdoc/&gt;</c>: telling whether a comment inherits, and putting what it inherits
/// together with what it has of its own. Finding the member it inherits from is a compiler question and is
/// answered where the compiler is at hand.
/// <para>
/// The rules are the ones the C# compiler and the documentation tools follow: the comment's own sections win,
/// and every section it lacks comes from the inherited comment. Sections that come once (summary, remarks,
/// returns, value) are matched by name; parameters and type parameters by their name, exceptions by their
/// type. A section holding nothing but <c>&lt;inheritdoc/&gt;</c> counts as lacking.
/// </para>
/// </summary>
public static class InheritedDocumentation
{
    private const string InheritDoc = "inheritdoc";

    /// <summary>
    /// Whether <paramref name="xml"/> asks to inherit, and from which member when it names one: the
    /// documentation id in the first <c>cref</c> found, or <see langword="null"/> when it leaves the choice to
    /// the member's base. Malformed input does not inherit.
    /// </summary>
    public static bool Inherits(string xml, out string? cref)
    {
        cref = null;

        if (xml.IndexOf(InheritDoc, StringComparison.Ordinal) < 0 || Parse(xml) is not XElement member)
        {
            return false;
        }

        List<XElement> markers = [.. member.Descendants(InheritDoc)];

        if (markers.Count == 0)
        {
            return false;
        }

        // A reference the compiler could not resolve is written "!:Name"; there is nothing to look up then.
        cref = markers
            .Select(marker => marker.Attribute("cref")?.Value)
            .FirstOrDefault(value => value is { Length: > 2 } && value[1] == ':' && value[0] != '!');

        return true;
    }

    /// <summary>
    /// <paramref name="own"/> with every section it lacks taken from <paramref name="inherited"/>, and without
    /// its <c>&lt;inheritdoc/&gt;</c> markers. Either input malformed leaves <paramref name="own"/> as it is.
    /// </summary>
    public static string Merge(string own, string inherited)
    {
        if (Parse(own) is not XElement member || Parse(inherited) is not XElement source)
        {
            return own;
        }

        foreach (XElement marker in member.Descendants(InheritDoc).ToList())
        {
            XElement? section = marker.Parent;
            marker.Remove();

            // A section that held only the marker is a section the comment does not have.
            if (section is not null && section != member && section.Nodes().All(IsBlank))
            {
                section.Remove();
            }
        }

        HashSet<string> present = new(member.Elements().Select(KeyOf), StringComparer.Ordinal);

        foreach (XElement section in source.Elements())
        {
            if (section.Name.LocalName != InheritDoc && present.Add(KeyOf(section)))
            {
                member.Add(new XElement(section));
            }
        }

        return XmlFragments.Write(member);
    }

    private static XElement? Parse(string xml)
    {
        try
        {
            return XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static bool IsBlank(XNode node) => node is XText text && string.IsNullOrWhiteSpace(text.Value);

    // What makes two sections the same section: "summary", "param:count", "exception:T:System.IO.IOException".
    private static string KeyOf(XElement section) => section.Name.LocalName switch
    {
        "param" or "typeparam" => section.Name.LocalName + ":" + section.Attribute("name")?.Value,
        "exception" or "permission" or "seealso" => section.Name.LocalName + ":" + section.Attribute("cref")?.Value,
        _ => section.Name.LocalName,
    };
}