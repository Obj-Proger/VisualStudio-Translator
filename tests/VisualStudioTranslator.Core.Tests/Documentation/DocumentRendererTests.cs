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
    public void RenderParagraphs_LangwordIsAKeywordAndParamrefsAreReferences()
    {
        Section section = ParagraphOf(
            new Ref { Kind = RefKind.Langword, Target = "null" },
            new Ref { Kind = RefKind.Paramref, Target = "value" },
            new Ref { Kind = RefKind.Typeparamref, Target = "T" });

        DocumentRenderer.RenderParagraphs(section)[0].Should().Equal(
            new RenderedRun("null", RunStyle.Keyword),
            new RenderedRun("value", RunStyle.Reference, ReferenceKind.Parameter),
            new RenderedRun("T", RunStyle.Reference, ReferenceKind.TypeParameter));
    }

    [Theory]
    [InlineData("T:System.String", "String")]
    [InlineData("M:Foo.Bar(System.Int32,System.String)", "Bar")]
    [InlineData("P:Foo.Bar.Baz", "Baz")]
    [InlineData("T:System.Collections.Generic.List`1", "List")]
    [InlineData("M:Foo.Bar``1(``0)", "Bar")]
    [InlineData("M:Foo.Bar.#ctor(System.Int32)", "Bar")]
    [InlineData("String", "String")] // unresolved, no kind prefix
    public void RenderParagraphs_UnresolvedCref_ShowsAGuessedShortName(string target, string expected)
    {
        Section section = ParagraphOf(new Ref { Kind = RefKind.Cref, Target = target });

        DocumentRenderer.RenderParagraphs(section)[0].Should().Equal(
            new RenderedRun(expected, RunStyle.Reference, ReferenceKind.Unknown));
    }

    [Fact]
    public void RenderParagraphs_ResolvedCref_ShowsTheResolvedTextAndKind()
    {
        // The id has lost the name of the type parameter; resolving it brings it back.
        Dictionary<string, ResolvedReference> references = new()
        {
            ["T:Ns.Result`1"] = new ResolvedReference("Result<TValue>", ReferenceKind.Class),
        };
        Section section = ParagraphOf(new Ref { Kind = RefKind.Cref, Target = "T:Ns.Result`1" });

        DocumentRenderer.RenderParagraphs(section, references)[0].Should().Equal(
            new RenderedRun("Result<TValue>", RunStyle.Reference, ReferenceKind.Class));
    }

    [Fact]
    public void RenderParagraphs_CrefAbsentFromTheResolvedSet_FallsBackToTheGuessedName()
    {
        Dictionary<string, ResolvedReference> references = new()
        {
            ["T:Other"] = new ResolvedReference("Other", ReferenceKind.Class),
        };
        Section section = ParagraphOf(new Ref { Kind = RefKind.Cref, Target = "T:Ns.Result`1" });

        DocumentRenderer.RenderParagraphs(section, references)[0].Should().Equal(
            new RenderedRun("Result", RunStyle.Reference, ReferenceKind.Unknown));
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

    // --- CollectReferenceTargets ---

    [Fact]
    public void CollectReferenceTargets_FindsCrefsEverywhereOnceEach()
    {
        Section summary = ParagraphOf(
            new Ref { Kind = RefKind.Cref, Target = "T:A" },
            new EmphasisRun { Content = [new Ref { Kind = RefKind.Cref, Target = "T:B" }] },
            new Ref { Kind = RefKind.Langword, Target = "null" },
            new Ref { Kind = RefKind.Paramref, Target = "p" });

        Section remarks = SectionOf(new ListBlock
        {
            Kind = ListKind.Bullet,
            Items =
            [
                new ListItem
                {
                    Term = [],
                    Description = [new Ref { Kind = RefKind.Cref, Target = "T:A" }, new Ref { Kind = RefKind.Cref, Target = "T:C" }],
                },
            ],
        });

        DocumentRenderer.CollectReferenceTargets(summary, null, remarks).Should().Equal("T:A", "T:B", "T:C");
    }

    [Fact]
    public void CollectReferenceTargets_NothingToCollect_IsEmpty()
    {
        DocumentRenderer.CollectReferenceTargets(null, ParagraphOf(new TextRun { Text = "plain" })).Should().BeEmpty();
    }
}