using AwesomeAssertions;
using VisualStudioTranslator.Core.Documentation;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Documentation;

public sealed class DocumentationXmlParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrBlankInput_ReturnsEmptyModel(string? xml)
    {
        DocumentModel result = DocumentationXmlParser.Parse(xml);

        result.Should().BeEquivalentTo(new DocumentModel());
    }

    [Fact]
    public void Parse_MalformedXml_ReturnsEmptyModelInsteadOfThrowing()
    {
        DocumentModel result = DocumentationXmlParser.Parse("<member><summary>unterminated");

        result.Should().BeEquivalentTo(new DocumentModel());
    }

    [Fact]
    public void Parse_SimpleSummary_ProducesSingleParagraphWithTrimmedText()
    {
        const string xml = """
            <member name="T:X">
                <summary>
                    Does a thing.
                </summary>
            </member>
            """;

        DocumentModel result = DocumentationXmlParser.Parse(xml);

        result.Summary.Should().BeEquivalentTo(new Section
        {
            Blocks =
            [
                new Paragraph { Inlines = [new TextRun { Text = "Does a thing." }] },
            ],
        });
    }

    [Fact]
    public void Parse_MultiLineText_CollapsesInternalWhitespaceToSingleSpaces()
    {
        const string xml = "<member><summary>Line one\n    and   line two.</summary></member>";

        DocumentModel result = DocumentationXmlParser.Parse(xml);

        Paragraph paragraph = (Paragraph)result.Summary!.Blocks.Single();
        TextRun text = (TextRun)paragraph.Inlines.Single();
        text.Text.Should().Be("Line one and line two.");
    }

    [Fact]
    public void Parse_InlineSeeCrefAndParamrefAndC_PreservesSurroundingTextAndProtectsMarkup()
    {
        const string xml = """
            <member>
                <summary>See <see cref="T:System.String"/> and <paramref name="value"/> or <c>true</c>.</summary>
            </member>
            """;

        DocumentModel result = DocumentationXmlParser.Parse(xml);

        result.Summary.Should().BeEquivalentTo(new Section
        {
            Blocks =
            [
                new Paragraph
                {
                    Inlines =
                    [
                        new TextRun { Text = "See " },
                        new Ref { Kind = RefKind.Cref, Target = "T:System.String" },
                        new TextRun { Text = " and " },
                        new Ref { Kind = RefKind.Paramref, Target = "value" },
                        new TextRun { Text = " or " },
                        new CodeSpan { Text = "true" },
                        new TextRun { Text = "." },
                    ],
                },
            ],
        });
    }

    [Fact]
    public void Parse_SeeWithDisplayText_PopulatesDisplayContent()
    {
        const string xml = """<member><summary>Read <see cref="T:System.String">the string type</see>.</summary></member>""";

        DocumentModel result = DocumentationXmlParser.Parse(xml);

        Paragraph paragraph = (Paragraph)result.Summary!.Blocks.Single();
        Ref reference = (Ref)paragraph.Inlines[1];

        reference.Kind.Should().Be(RefKind.Cref);
        reference.Target.Should().Be("T:System.String");
        reference.DisplayContent.Should().BeEquivalentTo(
        [
            new TextRun { Text = "the string type" },
        ]);
    }

    [Fact]
    public void Parse_SeeLangword_ProducesLangwordRef()
    {
        const string xml = """<member><summary>Returns <see langword="null"/> when missing.</summary></member>""";

        DocumentModel result = DocumentationXmlParser.Parse(xml);

        Paragraph paragraph = (Paragraph)result.Summary!.Blocks.Single();
        Ref reference = (Ref)paragraph.Inlines[1];

        reference.Kind.Should().Be(RefKind.Langword);
        reference.Target.Should().Be("null");
    }

    [Fact]
    public void Parse_MultiplePara_ProducesOneParagraphBlockEach()
    {
        const string xml = """
            <member>
                <remarks>
                    <para>First.</para>
                    <para>Second.</para>
                </remarks>
            </member>
            """;

        DocumentModel result = DocumentationXmlParser.Parse(xml);

        result.Remarks.Should().BeEquivalentTo(new Section
        {
            Blocks =
            [
                new Paragraph { Inlines = [new TextRun { Text = "First." }] },
                new Paragraph { Inlines = [new TextRun { Text = "Second." }] },
            ],
        });
    }

    [Fact]
    public void Parse_BulletList_ProducesListBlockWithDescriptionOnly()
    {
        const string xml = """
            <member>
                <remarks>
                    <list type="bullet">
                        <item><description>First item.</description></item>
                        <item><description>Second item.</description></item>
                    </list>
                </remarks>
            </member>
            """;

        DocumentModel result = DocumentationXmlParser.Parse(xml);

        result.Remarks.Should().BeEquivalentTo(new Section
        {
            Blocks =
            [
                new ListBlock
                {
                    Kind = ListKind.Bullet,
                    Items =
                    [
                        new ListItem { Term = [], Description = [new TextRun { Text = "First item." }] },
                        new ListItem { Term = [], Description = [new TextRun { Text = "Second item." }] },
                    ],
                },
            ],
        });
    }

    [Fact]
    public void Parse_TableList_PopulatesTermAndDescription()
    {
        const string xml = """
            <member>
                <remarks>
                    <list type="table">
                        <item><term>Key</term><description>Value.</description></item>
                    </list>
                </remarks>
            </member>
            """;

        DocumentModel result = DocumentationXmlParser.Parse(xml);

        ListBlock list = (ListBlock)result.Remarks!.Blocks.Single();
        list.Kind.Should().Be(ListKind.Table);
        list.Items.Single().Term.Should().BeEquivalentTo([new TextRun { Text = "Key" }]);
        list.Items.Single().Description.Should().BeEquivalentTo([new TextRun { Text = "Value." }]);
    }

    [Fact]
    public void Parse_CodeBlock_DedentsSharedLeadingWhitespaceAndTrimsBlankEdges()
    {
        const string xml = "<member><remarks><code>\n    var x = 1;\n    var y = 2;\n</code></remarks></member>";

        DocumentModel result = DocumentationXmlParser.Parse(xml);

        CodeBlock code = (CodeBlock)result.Remarks!.Blocks.Single();
        code.Code.Should().Be("var x = 1;\nvar y = 2;");
    }

    [Fact]
    public void Parse_CodeBlock_PreservesRelativeIndentationPastCommonPrefix()
    {
        const string xml = "<member><remarks><code>\n    if (x)\n        return;\n</code></remarks></member>";

        DocumentModel result = DocumentationXmlParser.Parse(xml);

        CodeBlock code = (CodeBlock)result.Remarks!.Blocks.Single();
        code.Code.Should().Be("if (x)\n    return;");
    }

    [Fact]
    public void Parse_ParamTypeParamAndException_PopulateNamedSections()
    {
        const string xml = """
            <member>
                <param name="value">The value.</param>
                <typeparam name="T">The element type.</typeparam>
                <exception cref="T:System.ArgumentNullException">value is null.</exception>
            </member>
            """;

        DocumentModel result = DocumentationXmlParser.Parse(xml);

        result.Params.Should().BeEquivalentTo(
        [
            new NamedSection
            {
                Name = "value",
                Content = new Section { Blocks = [new Paragraph { Inlines = [new TextRun { Text = "The value." }] }] },
            },
        ]);
        result.TypeParams.Should().BeEquivalentTo(
        [
            new NamedSection
            {
                Name = "T",
                Content = new Section { Blocks = [new Paragraph { Inlines = [new TextRun { Text = "The element type." }] }] },
            },
        ]);
        result.Exceptions.Should().BeEquivalentTo(
        [
            new NamedSection
            {
                Name = "T:System.ArgumentNullException",
                Content = new Section { Blocks = [new Paragraph { Inlines = [new TextRun { Text = "value is null." }] }] },
            },
        ]);
    }

    [Fact]
    public void Parse_SeeAlsoAtTopLevel_PopulatesSeeAlsoList()
    {
        const string xml = """
            <member>
                <summary>Text.</summary>
                <seealso cref="T:System.String"/>
                <seealso cref="T:System.Int32"/>
            </member>
            """;

        DocumentModel result = DocumentationXmlParser.Parse(xml);

        result.SeeAlso.Should().BeEquivalentTo(["T:System.String", "T:System.Int32"]);
    }

    [Fact]
    public void Parse_SummaryContainingOnlyInheritdoc_LeavesSummaryNull()
    {
        const string xml = "<member><summary><inheritdoc/></summary></member>";

        DocumentModel result = DocumentationXmlParser.Parse(xml);

        result.Summary.Should().BeNull();
    }

    [Fact]
    public void Parse_MemberIsBareInheritdoc_ReturnsModelWithNoSections()
    {
        const string xml = "<member><inheritdoc/></member>";

        DocumentModel result = DocumentationXmlParser.Parse(xml);

        result.Should().BeEquivalentTo(new DocumentModel());
    }

    [Fact]
    public void Parse_EmphasisElements_WrapNestedTextInEmphasisRun()
    {
        const string xml = "<member><summary>This is <b>bold</b> and <i>italic</i>.</summary></member>";

        DocumentModel result = DocumentationXmlParser.Parse(xml);

        result.Summary.Should().BeEquivalentTo(new Section
        {
            Blocks =
            [
                new Paragraph
                {
                    Inlines =
                    [
                        new TextRun { Text = "This is " },
                        new EmphasisRun { Content = [new TextRun { Text = "bold" }] },
                        new TextRun { Text = " and " },
                        new EmphasisRun { Content = [new TextRun { Text = "italic" }] },
                        new TextRun { Text = "." },
                    ],
                },
            ],
        });
    }

    [Fact]
    public void Parse_LooseTextMixedWithPara_KeepsLooseTextAsItsOwnParagraph()
    {
        const string xml = """
            <member>
                <remarks>
                    Loose intro text.
                    <para>Explicit paragraph.</para>
                </remarks>
            </member>
            """;

        DocumentModel result = DocumentationXmlParser.Parse(xml);

        result.Remarks.Should().BeEquivalentTo(new Section
        {
            Blocks =
            [
                new Paragraph { Inlines = [new TextRun { Text = "Loose intro text." }] },
                new Paragraph { Inlines = [new TextRun { Text = "Explicit paragraph." }] },
            ],
        });
    }
}