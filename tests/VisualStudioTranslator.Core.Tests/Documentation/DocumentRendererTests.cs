using AwesomeAssertions;
using VisualStudioTranslator.Core.Documentation;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Documentation;

public sealed class DocumentRendererTests
{
    private static Section SectionOf(params Block[] blocks) => new() { Blocks = blocks };

    private static Section ParagraphOf(params Inline[] inlines) =>
        SectionOf(new Paragraph { Inlines = inlines });

    [Fact]
    public void RenderParagraphs_NullSection_IsEmpty()
    {
        DocumentRenderer.RenderParagraphs(null).Should().BeEmpty();
    }

    [Fact]
    public void RenderParagraphs_TextAndCode_ProduceOneParagraphOfStyledRuns()
    {
        Section section = ParagraphOf(
            new TextRun { Text = "Use " },
            new CodeSpan { Text = "List<T>" },
            new TextRun { Text = " here." });

        IReadOnlyList<IReadOnlyList<RenderedRun>> paragraphs = DocumentRenderer.RenderParagraphs(section);

        paragraphs.Should().HaveCount(1);
        paragraphs[0].Should().Equal(
            new RenderedRun("Use ", RunStyle.Text),
            new RenderedRun("List<T>", RunStyle.Code),
            new RenderedRun(" here.", RunStyle.Text));
    }

    [Fact]
    public void RenderParagraphs_EachParagraphBecomesItsOwnEntry()
    {
        Section section = SectionOf(
            new Paragraph { Inlines = [new TextRun { Text = "First." }] },
            new Paragraph { Inlines = [new TextRun { Text = "Second." }] });

        DocumentRenderer.RenderParagraphs(section).Should().HaveCount(2);
    }

    [Fact]
    public void RenderParagraphs_Emphasis_EmphasizesTextButNotCodeInsideIt()
    {
        Section section = ParagraphOf(
            new EmphasisRun { Content = [new TextRun { Text = "very " }, new CodeSpan { Text = "x" }] });

        DocumentRenderer.RenderParagraphs(section)[0].Should().Equal(
            new RenderedRun("very ", RunStyle.Emphasis),
            new RenderedRun("x", RunStyle.Code));
    }

    [Fact]
    public void RenderParagraphs_EmptyTextRun_IsSkipped()
    {
        Section section = ParagraphOf(new TextRun { Text = string.Empty }, new TextRun { Text = "x" });

        DocumentRenderer.RenderParagraphs(section)[0].Should().Equal(new RenderedRun("x", RunStyle.Text));
    }

    [Fact]
    public void RenderParagraphs_LangwordIsAKeywordAndParamrefsAreCode()
    {
        Section section = ParagraphOf(
            new Ref { Kind = RefKind.Langword, Target = "null" },
            new Ref { Kind = RefKind.Paramref, Target = "value" },
            new Ref { Kind = RefKind.Typeparamref, Target = "T" });

        DocumentRenderer.RenderParagraphs(section)[0].Should().Equal(
            new RenderedRun("null", RunStyle.Keyword),
            new RenderedRun("value", RunStyle.Code),
            new RenderedRun("T", RunStyle.Code));
    }

    [Theory]
    [InlineData("T:System.String", "String")]
    [InlineData("M:Foo.Bar(System.Int32,System.String)", "Bar")]
    [InlineData("P:Foo.Bar.Baz", "Baz")]
    [InlineData("T:System.Collections.Generic.List`1", "List")]
    [InlineData("M:Foo.Bar``1(``0)", "Bar")]
    [InlineData("M:Foo.Bar.#ctor(System.Int32)", "Bar")]
    [InlineData("String", "String")] // unresolved, no kind prefix
    public void RenderParagraphs_CrefWithoutDisplayText_ShowsTheShortName(string target, string expected)
    {
        Section section = ParagraphOf(new Ref { Kind = RefKind.Cref, Target = target });

        DocumentRenderer.RenderParagraphs(section)[0].Should().Equal(new RenderedRun(expected, RunStyle.Code));
    }

    [Fact]
    public void RenderParagraphs_CrefWithDisplayText_ShowsThatText()
    {
        Section section = ParagraphOf(new Ref
        {
            Kind = RefKind.Cref,
            Target = "T:System.String",
            DisplayContent = [new TextRun { Text = "the string type" }],
        });

        DocumentRenderer.RenderParagraphs(section)[0].Should().Equal(new RenderedRun("the string type", RunStyle.Text));
    }

    [Fact]
    public void RenderParagraphs_BulletList_PrefixesEachItem()
    {
        Section section = SectionOf(new ListBlock
        {
            Kind = ListKind.Bullet,
            Items =
            [
                new ListItem { Term = [], Description = [new TextRun { Text = "one" }] },
                new ListItem { Term = [], Description = [new TextRun { Text = "two" }] },
            ],
        });

        IReadOnlyList<IReadOnlyList<RenderedRun>> paragraphs = DocumentRenderer.RenderParagraphs(section);

        paragraphs.Should().HaveCount(2);
        paragraphs[0].Should().Equal(new RenderedRun("• ", RunStyle.Text), new RenderedRun("one", RunStyle.Text));
        paragraphs[1].Should().Equal(new RenderedRun("• ", RunStyle.Text), new RenderedRun("two", RunStyle.Text));
    }

    [Fact]
    public void RenderParagraphs_NumberedListWithTerms_NumbersItemsAndSeparatesTermFromDescription()
    {
        Section section = SectionOf(new ListBlock
        {
            Kind = ListKind.Number,
            Items =
            [
                new ListItem { Term = [new TextRun { Text = "a" }], Description = [new TextRun { Text = "first" }] },
                new ListItem { Term = [new TextRun { Text = "b" }], Description = [new TextRun { Text = "second" }] },
            ],
        });

        IReadOnlyList<IReadOnlyList<RenderedRun>> paragraphs = DocumentRenderer.RenderParagraphs(section);

        paragraphs[1].Should().Equal(
            new RenderedRun("2. ", RunStyle.Text),
            new RenderedRun("b", RunStyle.Text),
            new RenderedRun(" — ", RunStyle.Text),
            new RenderedRun("second", RunStyle.Text));
    }

    [Fact]
    public void RenderParagraphs_CodeBlock_IsOneCodeRun()
    {
        Section section = SectionOf(new CodeBlock { Code = "var x = 1;\nreturn x;" });

        DocumentRenderer.RenderParagraphs(section)[0].Should().Equal(
            new RenderedRun("var x = 1;\nreturn x;", RunStyle.Code));
    }
}