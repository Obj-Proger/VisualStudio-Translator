using AwesomeAssertions;
using VisualStudioTranslator.Core.Documentation;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Documentation;

public sealed class DocumentationXmlSectionsTests
{
    private static readonly string[] Tooltip = ["summary", "remarks", "returns", "value"];

    private const string Method =
        "<member name=\"M:Lib.Parser.Parse(System.String)\">" +
        "<summary>Parses <paramref name=\"text\"/>.</summary>" +
        "<param name=\"text\">The text to parse.</param>" +
        "<returns>The parsed <see cref=\"T:Lib.Node\"/>.</returns>" +
        "<exception cref=\"T:System.FormatException\">The text is not valid.</exception>" +
        "<remarks>Never returns <c>null</c>.</remarks>" +
        "<example>var node = Parse(\"1\");</example>" +
        "</member>";

    [Fact]
    public void Keep_DropsEverySectionNotAskedFor()
    {
        string result = DocumentationXmlSections.Keep(Method, Tooltip);

        DocumentModel model = DocumentationXmlParser.Parse(result);
        model.Params.Should().BeEmpty();
        model.Exceptions.Should().BeEmpty();
        model.Examples.Should().BeEmpty();
    }

    [Fact]
    public void Keep_LeavesTheSectionsAskedForUntouchedAndInTheirOrder()
    {
        string result = DocumentationXmlSections.Keep(Method, Tooltip);

        result.Should().Be(
            "<member name=\"M:Lib.Parser.Parse(System.String)\">" +
            "<summary>Parses <paramref name=\"text\" />.</summary>" +
            "<returns>The parsed <see cref=\"T:Lib.Node\" />.</returns>" +
            "<remarks>Never returns <c>null</c>.</remarks>" +
            "</member>");
    }

    [Fact]
    public void Keep_KeepsTheWhitespaceBetweenSections()
    {
        const string xml = "<member>\n    <summary>Gets the name.</summary>\n    <param name=\"x\">X.</param>\n</member>";

        DocumentationXmlSections.Keep(xml, Tooltip).Should().Be("<member>\n    <summary>Gets the name.</summary>\n    \n</member>");
    }

    [Fact]
    public void Keep_NothingToDrop_ReturnsTheInputAsItIs()
    {
        const string xml = "<member name=\"P:Lib.Node.Name\">\n  <summary>Gets the name.</summary>\n</member>";

        DocumentationXmlSections.Keep(xml, Tooltip).Should().BeSameAs(xml);
    }

    [Fact]
    public void Keep_MalformedXml_ReturnsTheInputAsItIs()
    {
        const string xml = "<member><summary>unterminated";

        DocumentationXmlSections.Keep(xml, Tooltip).Should().BeSameAs(xml);
    }

    [Fact]
    public void Keep_ElementInsideASection_IsNeverTakenForASection()
    {
        const string xml = "<member><summary>See <param>x</param> and <example>y</example>.</summary></member>";

        DocumentationXmlSections.Keep(xml, Tooltip).Should().BeSameAs(xml);
    }
}