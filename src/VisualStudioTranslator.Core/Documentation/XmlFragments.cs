using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace VisualStudioTranslator.Core.Documentation;

/// <summary>
/// Writes an element back to text exactly as it reads: line breaks are left as they are, where
/// <see cref="XNode.ToString(SaveOptions)"/> turns every one into the platform's own and so changes the text
/// on Windows.
/// </summary>
internal static class XmlFragments
{
    public static string Write(XElement element)
    {
        XmlWriterSettings settings = new()
        {
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.None,
            ConformanceLevel = ConformanceLevel.Fragment,
        };

        StringBuilder text = new();

        using (XmlWriter writer = XmlWriter.Create(text, settings))
        {
            element.WriteTo(writer);
        }

        return text.ToString();
    }
}