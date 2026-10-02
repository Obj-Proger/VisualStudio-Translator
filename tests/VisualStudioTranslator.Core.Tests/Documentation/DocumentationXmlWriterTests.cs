using AwesomeAssertions;
using VisualStudioTranslator.Core.Documentation;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Documentation;

public sealed class DocumentationXmlWriterTests
{
    private static DocumentModel SummaryOf(params Inline[] inlines) => new()
    {
        Summary = new Section { Blocks = [new Paragraph { Inlines = inlines }] },
    };

    // Parses, writes, parses again, and expects the same model. The guard against an empty
    // model matters: the parser returns one for malformed XML, which would make a broken test
    // sample pass for the wrong reason.
    private static void AssertRoundTrips(string xml)
    {
        DocumentModel model = DocumentationXmlParser.Parse(xml);
        DocumentSegmenter.Segment(model).Should().NotBeEmpty("the sample must actually parse");

        DocumentModel reparsed = DocumentationXmlParser.Parse(DocumentationXmlWriter.Write(model));

        reparsed.Should().BeEquivalentTo(model, options => options.PreferringRuntimeMemberTypes());
    }

    [Fact]
    public void Write_EmptyModel_ProducesAnEmptyMember()
    {
        DocumentationXmlWriter.Write(new DocumentModel()).Should().Be("<member />");
    }

    [Fact]
    public void Write_SingleParagraph_IsCompactWithNoWhitespaceBetweenElements()
    {
        DocumentationXmlWriter.Write(SummaryOf(new TextRun { Text = "Hello." }))
            .Should().Be("<member><summary><para>Hello.</para></summary></member>");
    }

    [Fact]
    public void Write_CodeSpanAndSpecialCharacters_AreEscaped()
    {
        DocumentModel model = SummaryOf(
            new TextRun { Text = "Use " },
            new CodeSpan { Text = "List<T>" },
            new TextRun { Text = " & more." });

        DocumentationXmlWriter.Write(model)
            .Should().Be("<member><summary><para>Use <c>List&lt;T&gt;</c> &amp; more.</para></summary></member>");
    }

    [Fact]
    public void Write_Emphasis_IsWrittenAsItalic()
    {
        DocumentModel model = SummaryOf(
            new TextRun { Text = "This is " },
            new EmphasisRun { Content = [new TextRun { Text = "important" }] },
            new TextRun { Text = "." });

        DocumentationXmlWriter.Write(model)
            .Should().Be("<member><summary><para>This is <i>important</i>.</para></summary></member>");
    }

    [Fact]
    public void Write_List_UsesTypeTermAndDescription()
    {
        DocumentModel model = new()
        {
            Remarks = new Section
            {
                Blocks =
                [
                    new ListBlock
                    {
                        Kind = ListKind.Bullet,
                        Items = [new ListItem { Term = [new TextRun { Text = "a" }], Description = [new TextRun { Text = "b" }] }],
                    },
                ],
            },
        };

        DocumentationXmlWriter.Write(model).Should().Be(
            "<member><remarks><list type=\"bullet\"><item><term>a</term><description>b</description></item></list></remarks></member>");
    }

    [Fact]
    public void Write_AdjacentTextRuns_AreMergedWhenReadBack()
    {
        // What Restore produces around a glossary term: separate runs that read as one sentence.
        DocumentModel model = SummaryOf(
            new TextRun { Text = "Получает " },
            new TextRun { Text = "Roslyn" },
            new TextRun { Text = " экземпляр." });

        DocumentModel reparsed = DocumentationXmlParser.Parse(DocumentationXmlWriter.Write(model));

        DocumentSegmenter.Segment(reparsed).Single().Inlines
            .Should().BeEquivalentTo([new TextRun { Text = "Получает Roslyn экземпляр." }]);
    }

    [Fact]
    public void Write_ExtraWhitespaceInText_IsNormalizedWhenReadBack()
    {
        DocumentModel reparsed = DocumentationXmlParser.Parse(
            DocumentationXmlWriter.Write(SummaryOf(new TextRun { Text = "  a   b  " })));

        DocumentSegmenter.Segment(reparsed).Single().Inlines
            .Should().BeEquivalentTo([new TextRun { Text = "a b" }]);
    }

    [Theory]
    [InlineData(0x0007)] // a control character
    [InlineData(0xD800)] // an unpaired surrogate
    public void Write_CharacterXmlCannotHold_IsDroppedWithoutThrowing(int codeUnit)
    {
        // Passed as a code unit rather than a string: xUnit serializes theory arguments,
        // and a lone surrogate does not survive that - it would arrive as U+FFFD, which
        // XML allows, and the test would be checking something else.
        string xml = string.Empty;
        Action act = () => xml = DocumentationXmlWriter.Write(SummaryOf(new TextRun { Text = $"x{(char)codeUnit}y" }));

        act.Should().NotThrow();
        DocumentSegmenter.Segment(DocumentationXmlParser.Parse(xml)).Single().Inlines
            .Should().BeEquivalentTo([new TextRun { Text = "xy" }]);
    }

    [Fact]
    public void Write_ValidSurrogatePair_IsKept()
    {
        DocumentModel reparsed = DocumentationXmlParser.Parse(
            DocumentationXmlWriter.Write(SummaryOf(new TextRun { Text = "emoji \uD83D\uDE00 stays" })));

        DocumentSegmenter.Segment(reparsed).Single().Inlines
            .Should().BeEquivalentTo([new TextRun { Text = "emoji \uD83D\uDE00 stays" }]);
    }

    // --- Parsing the output gives the model back ---

    [Fact]
    public void RoundTrip_EveryInlineKind()
    {
        AssertRoundTrips(
            "<member name=\"M:X\"><summary>Returns <c>x</c> for <see cref=\"T:Foo\"/>, <see langword=\"null\"/>, "
            + "<paramref name=\"p\"/>, <typeparamref name=\"T\"/> and <see cref=\"T:Bar\">the bar</see> "
            + "in <b>bold <i>nested</i></b> text.</summary></member>");
    }

    [Fact]
    public void RoundTrip_ParagraphsListsAndCode()
    {
        AssertRoundTrips(
            "<member><remarks><para>First paragraph.</para><para>Second with <c>code</c>.</para>"
            + "<list type=\"number\"><item><term>One</term><description>First <c>x</c></description></item>"
            + "<item><description>No term</description></item></list>"
            + "<code lang=\"csharp\">\n    var x = 1;\n    if (x &gt; 0) { }\n</code></remarks></member>");
    }

    [Fact]
    public void RoundTrip_NamedSectionsExamplesAndSeeAlso()
    {
        AssertRoundTrips(
            "<member><summary>S.</summary><returns>R.</returns><value>V.</value>"
            + "<param name=\"a\">Param a.</param><param name=\"b\">Param <paramref name=\"a\"/>.</param>"
            + "<typeparam name=\"T\">The type.</typeparam>"
            + "<exception cref=\"T:System.ArgumentNullException\">Thrown when <paramref name=\"a\"/> is <see langword=\"null\"/>.</exception>"
            + "<example><para>Example text.</para><code>Foo();</code></example><seealso cref=\"T:Other\"/></member>");
    }

    [Fact]
    public void RoundTrip_SpecialCharactersAndNonLatinText()
    {
        AssertRoundTrips("<member><summary>a &lt; b &amp; c &quot;quoted&quot; &apos;x&apos;. Возвращает значение.</summary></member>");
    }
}