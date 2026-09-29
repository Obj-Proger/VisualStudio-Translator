using AwesomeAssertions;
using VisualStudioTranslator.Core.Documentation;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Documentation;

public sealed class DocumentSegmenterTests
{
    [Fact]
    public void Segment_SingleParagraphSummary_ProducesOneSegment()
    {
        DocumentModel model = new()
        {
            Summary = new Section { Blocks = [new Paragraph { Inlines = [new TextRun { Text = "Hello." }] }] },
        };

        IReadOnlyList<Segment> segments = DocumentSegmenter.Segment(model);

        segments.Should().BeEquivalentTo(
        [
            new Segment { Id = 0, Inlines = [new TextRun { Text = "Hello." }] },
        ]);
    }

    [Fact]
    public void Segment_SummaryThenRemarks_AssignsIdsInDeclarationOrder()
    {
        DocumentModel model = new()
        {
            Summary = new Section { Blocks = [new Paragraph { Inlines = [new TextRun { Text = "Summary text." }] }] },
            Remarks = new Section { Blocks = [new Paragraph { Inlines = [new TextRun { Text = "Remarks text." }] }] },
        };

        IReadOnlyList<Segment> segments = DocumentSegmenter.Segment(model);

        segments.Should().HaveCount(2);
        segments[0].Id.Should().Be(0);
        ((TextRun)segments[0].Inlines.Single()).Text.Should().Be("Summary text.");
        segments[1].Id.Should().Be(1);
        ((TextRun)segments[1].Inlines.Single()).Text.Should().Be("Remarks text.");
    }

    [Fact]
    public void Segment_MultipleParagraphsInOneSection_AssignsOneIdEach()
    {
        DocumentModel model = new()
        {
            Remarks = new Section
            {
                Blocks =
                [
                    new Paragraph { Inlines = [new TextRun { Text = "First." }] },
                    new Paragraph { Inlines = [new TextRun { Text = "Second." }] },
                ],
            },
        };

        IReadOnlyList<Segment> segments = DocumentSegmenter.Segment(model);

        segments.Select(s => s.Id).Should().BeEquivalentTo([0, 1], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Segment_CodeBlock_IsNeverASegment()
    {
        DocumentModel model = new()
        {
            Remarks = new Section
            {
                Blocks =
                [
                    new Paragraph { Inlines = [new TextRun { Text = "Before." }] },
                    new CodeBlock { Code = "var x = 1;" },
                    new Paragraph { Inlines = [new TextRun { Text = "After." }] },
                ],
            },
        };

        IReadOnlyList<Segment> segments = DocumentSegmenter.Segment(model);

        segments.Should().HaveCount(2);
        ((TextRun)segments[0].Inlines.Single()).Text.Should().Be("Before.");
        ((TextRun)segments[1].Inlines.Single()).Text.Should().Be("After.");
    }

    [Fact]
    public void Segment_EmptyParagraph_IsSkippedDefensively()
    {
        DocumentModel model = new()
        {
            Remarks = new Section
            {
                Blocks =
                [
                    new Paragraph { Inlines = [] },
                    new Paragraph { Inlines = [new TextRun { Text = "Only real one." }] },
                ],
            },
        };

        IReadOnlyList<Segment> segments = DocumentSegmenter.Segment(model);

        segments.Should().ContainSingle();
        ((TextRun)segments[0].Inlines.Single()).Text.Should().Be("Only real one.");
    }

    [Fact]
    public void Segment_ListWithDescriptionOnly_SegmentsDescriptionButNotEmptyTerm()
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
                        Items =
                        [
                            new ListItem { Term = [], Description = [new TextRun { Text = "First item." }] },
                            new ListItem { Term = [], Description = [new TextRun { Text = "Second item." }] },
                        ],
                    },
                ],
            },
        };

        IReadOnlyList<Segment> segments = DocumentSegmenter.Segment(model);

        segments.Should().HaveCount(2);
        ((TextRun)segments[0].Inlines.Single()).Text.Should().Be("First item.");
        ((TextRun)segments[1].Inlines.Single()).Text.Should().Be("Second item.");
    }

    [Fact]
    public void Segment_TableListItem_SegmentsTermBeforeDescription()
    {
        DocumentModel model = new()
        {
            Remarks = new Section
            {
                Blocks =
                [
                    new ListBlock
                    {
                        Kind = ListKind.Table,
                        Items =
                        [
                            new ListItem
                            {
                                Term = [new TextRun { Text = "Key" }],
                                Description = [new TextRun { Text = "Value." }],
                            },
                        ],
                    },
                ],
            },
        };

        IReadOnlyList<Segment> segments = DocumentSegmenter.Segment(model);

        segments.Should().HaveCount(2);
        ((TextRun)segments[0].Inlines.Single()).Text.Should().Be("Key");
        ((TextRun)segments[1].Inlines.Single()).Text.Should().Be("Value.");
    }

    [Fact]
    public void Segment_ParamsInOrder_SegmentsEachEntrysContent()
    {
        DocumentModel model = new()
        {
            Params =
            [
                new NamedSection
                {
                    Name = "first",
                    Content = new Section { Blocks = [new Paragraph { Inlines = [new TextRun { Text = "First param." }] }] },
                },
                new NamedSection
                {
                    Name = "second",
                    Content = new Section { Blocks = [new Paragraph { Inlines = [new TextRun { Text = "Second param." }] }] },
                },
            ],
        };

        IReadOnlyList<Segment> segments = DocumentSegmenter.Segment(model);

        segments.Should().HaveCount(2);
        ((TextRun)segments[0].Inlines.Single()).Text.Should().Be("First param.");
        ((TextRun)segments[1].Inlines.Single()).Text.Should().Be("Second param.");
    }

    [Fact]
    public void Segment_Examples_AreSegmented()
    {
        DocumentModel model = new()
        {
            Examples = [new Section { Blocks = [new Paragraph { Inlines = [new TextRun { Text = "Example text." }] }] }],
        };

        IReadOnlyList<Segment> segments = DocumentSegmenter.Segment(model);

        segments.Should().ContainSingle();
        ((TextRun)segments[0].Inlines.Single()).Text.Should().Be("Example text.");
    }

    [Fact]
    public void Compose_EmptyTranslations_ReproducesOriginalStructure()
    {
        DocumentModel model = new()
        {
            Summary = new Section { Blocks = [new Paragraph { Inlines = [new TextRun { Text = "Hello." }] }] },
        };

        DocumentModel result = DocumentSegmenter.Compose(model, new Dictionary<int, IReadOnlyList<Inline>>());

        result.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void Compose_TranslationForOneOfTwoSegments_ReplacesOnlyThatOne()
    {
        DocumentModel model = new()
        {
            Summary = new Section { Blocks = [new Paragraph { Inlines = [new TextRun { Text = "Original summary." }] }] },
            Remarks = new Section { Blocks = [new Paragraph { Inlines = [new TextRun { Text = "Original remarks." }] }] },
        };

        Dictionary<int, IReadOnlyList<Inline>> translations = new()
        {
            [0] = [new TextRun { Text = "Translated summary." }],
        };

        DocumentModel result = DocumentSegmenter.Compose(model, translations);

        ((TextRun)((Paragraph)result.Summary!.Blocks[0]).Inlines.Single()).Text.Should().Be("Translated summary.");
        // Segment 1 (remarks) had no translation and is left exactly as it was -
        // a failed or missing translation degrades to the original text, not a gap.
        ((TextRun)((Paragraph)result.Remarks!.Blocks[0]).Inlines.Single()).Text.Should().Be("Original remarks.");
    }

    [Fact]
    public void Compose_LeavesCodeBlocksAndSeeAlsoUntouched()
    {
        DocumentModel model = new()
        {
            Remarks = new Section { Blocks = [new CodeBlock { Code = "var x = 1;" }] },
            SeeAlso = ["T:System.String"],
        };

        DocumentModel result = DocumentSegmenter.Compose(model, new Dictionary<int, IReadOnlyList<Inline>>());

        result.Remarks.Should().BeEquivalentTo(model.Remarks);
        result.SeeAlso.Should().BeEquivalentTo(model.SeeAlso);
    }

    [Fact]
    public void SegmentThenCompose_RoundTripsBackToATranslatedModel()
    {
        DocumentModel model = new()
        {
            Summary = new Section { Blocks = [new Paragraph { Inlines = [new TextRun { Text = "Hello." }] }] },
            Params =
            [
                new NamedSection
                {
                    Name = "value",
                    Content = new Section { Blocks = [new Paragraph { Inlines = [new TextRun { Text = "The value." }] }] },
                },
            ],
        };

        IReadOnlyList<Segment> segments = DocumentSegmenter.Segment(model);

        // Simulates a translation step: uppercase every TextRun in every segment.
        Dictionary<int, IReadOnlyList<Inline>> translations = segments.ToDictionary(
            s => s.Id,
            IReadOnlyList<Inline> (s) => [.. s.Inlines.Select(i => i is TextRun run ? run with { Text = run.Text.ToUpperInvariant() } : i)]);

        DocumentModel result = DocumentSegmenter.Compose(model, translations);

        ((TextRun)((Paragraph)result.Summary!.Blocks[0]).Inlines.Single()).Text.Should().Be("HELLO.");
        ((TextRun)((Paragraph)result.Params[0].Content.Blocks[0]).Inlines.Single()).Text.Should().Be("THE VALUE.");
    }
}