using AwesomeAssertions;
using VisualStudioTranslator.Core.Documentation;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Documentation;

public sealed class InheritedDocumentationTests
{
    private const string Base =
        "<member name=\"M:Lib.Shape.Area(System.Int32)\">" +
        "<summary>Computes the area.</summary>" +
        "<param name=\"scale\">The scale to apply.</param>" +
        "<returns>The area in square units.</returns>" +
        "<exception cref=\"T:System.ArgumentOutOfRangeException\">The scale is negative.</exception>" +
        "</member>";

    [Theory]
    [InlineData("<member><inheritdoc/></member>")]
    [InlineData("<member><summary><inheritdoc/></summary></member>")]
    [InlineData("<member><summary>Own.</summary><inheritdoc /></member>")]
    public void Inherits_CommentWithAMarker_IsTrueAndNamesNoMember(string xml)
    {
        InheritedDocumentation.Inherits(xml, out string? cref).Should().BeTrue();
        cref.Should().BeNull();
    }

    [Fact]
    public void Inherits_MarkerNamingAMember_GivesItsDocumentationId()
    {
        const string xml = "<member><inheritdoc cref=\"M:Lib.Shape.Area(System.Int32)\"/></member>";

        InheritedDocumentation.Inherits(xml, out string? cref).Should().BeTrue();

        cref.Should().Be("M:Lib.Shape.Area(System.Int32)");
    }

    [Fact]
    public void Inherits_ReferenceTheCompilerCouldNotResolve_NamesNoMember()
    {
        InheritedDocumentation.Inherits("<member><inheritdoc cref=\"!:Missing\"/></member>", out string? cref).Should().BeTrue();

        cref.Should().BeNull();
    }

    [Theory]
    [InlineData("<member><summary>Gets the name.</summary></member>")]
    [InlineData("<member><summary>Mentions inheritdoc in prose.</summary></member>")]
    [InlineData("<member><summary>unterminated <inheritdoc/>")]
    public void Inherits_CommentWithoutAMarker_IsFalse(string xml)
    {
        InheritedDocumentation.Inherits(xml, out _).Should().BeFalse();
    }

    [Fact]
    public void Merge_BareMarker_TakesEverySection()
    {
        string merged = InheritedDocumentation.Merge(
            "<member name=\"M:Lib.Square.Area(System.Int32)\"><inheritdoc/></member>", Base);

        DocumentModel model = DocumentationXmlParser.Parse(merged);
        Text(model.Summary).Should().Be("Computes the area.");
        Text(model.Returns).Should().Be("The area in square units.");
        model.Params.Should().ContainSingle().Which.Name.Should().Be("scale");
        model.Exceptions.Should().ContainSingle();
        merged.Should().NotContain("inheritdoc").And.StartWith("<member name=\"M:Lib.Square.Area(System.Int32)\">");
    }

    [Fact]
    public void Merge_OwnSections_WinOverInheritedOnes()
    {
        string merged = InheritedDocumentation.Merge(
            "<member><inheritdoc/><summary>Computes the area of the square.</summary>" +
            "<param name=\"scale\">Own scale.</param></member>",
            Base);

        DocumentModel model = DocumentationXmlParser.Parse(merged);
        Text(model.Summary).Should().Be("Computes the area of the square.");
        model.Params.Should().ContainSingle();
        Text(model.Params[0].Content).Should().Be("Own scale.");
        Text(model.Returns).Should().Be("The area in square units.");
    }

    [Fact]
    public void Merge_SectionHoldingOnlyAMarker_IsFilledFromTheInheritedComment()
    {
        string merged = InheritedDocumentation.Merge(
            "<member><summary> <inheritdoc/> </summary><remarks>Own.</remarks></member>", Base);

        DocumentModel model = DocumentationXmlParser.Parse(merged);
        Text(model.Summary).Should().Be("Computes the area.");
        Text(model.Remarks).Should().Be("Own.");
    }

    [Fact]
    public void Merge_ParametersAndExceptions_AreMatchedByWhatTheyName()
    {
        string merged = InheritedDocumentation.Merge(
            "<member><inheritdoc/><param name=\"offset\">The offset.</param>" +
            "<exception cref=\"T:System.InvalidOperationException\">Not ready.</exception></member>",
            Base);

        DocumentModel model = DocumentationXmlParser.Parse(merged);
        model.Params.Select(param => param.Name).Should().Equal("offset", "scale");
        model.Exceptions.Should().HaveCount(2);
    }

    [Fact]
    public void Merge_MarkersInTheInheritedComment_AreNotCopied()
    {
        string merged = InheritedDocumentation.Merge(
            "<member><inheritdoc/></member>", "<member><inheritdoc/><summary>S.</summary></member>");

        merged.Should().NotContain("inheritdoc");
    }

    [Theory]
    [InlineData("<member><inheritdoc/>", Base)]
    [InlineData("<member><inheritdoc/></member>", "<member><summary>unterminated")]
    public void Merge_MalformedInput_LeavesTheOwnCommentAsItIs(string own, string inherited)
    {
        InheritedDocumentation.Merge(own, inherited).Should().BeSameAs(own);
    }

    private static string Text(Section? section) => string.Concat(
        section!.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines).OfType<TextRun>().Select(run => run.Text));
}