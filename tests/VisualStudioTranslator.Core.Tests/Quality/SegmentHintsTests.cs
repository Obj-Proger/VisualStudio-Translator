using AwesomeAssertions;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Core.Quality;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Quality;

public sealed class SegmentHintsTests
{
    private static Segment Paragraph(int id, params Inline[] inlines) => new() { Id = id, Inlines = inlines };

    private static Segment Paragraph(int id, string text) => Paragraph(id, new TextRun { Text = text });

    [Fact]
    public void Codes_NameTheCodeOfEachStandAloneToken()
    {
        ProtectedSegment segment = MarkupProtector.Protect(
        [
            new TextRun { Text = "Ensures a copy of " },
            new Ref { Kind = RefKind.Paramref, Target = "sourceFolder" },
            new TextRun { Text = " exists in " },
            new CodeSpan { Text = "deploymentsRoot" },
            new TextRun { Text = "." },
        ]);

        SegmentHints.Codes(segment).Should().BeEquivalentTo(
            new Dictionary<int, string> { [0] = "sourceFolder", [1] = "deploymentsRoot" });
    }

    [Fact]
    public void Codes_Cref_IsTheNameAsATooltipShowsIt()
    {
        ProtectedSegment segment = MarkupProtector.Protect(
        [
            new TextRun { Text = "Like " },
            new Ref { Kind = RefKind.Cref, Target = "M:System.String.Join(System.String,System.String[])" },
            new TextRun { Text = " does." },
        ]);

        SegmentHints.Codes(segment).Values.Should().Equal("Join");
    }

    [Fact]
    public void Codes_EmphasisAroundText_IsNotCode()
    {
        ProtectedSegment segment = MarkupProtector.Protect(
        [
            new TextRun { Text = "Do " },
            new EmphasisRun { Content = [new TextRun { Text = "not" }] },
            new TextRun { Text = " call it twice." },
        ]);

        SegmentHints.Codes(segment).Should().BeEmpty();
    }

    [Fact]
    public void Around_MiddleParagraph_IsBetweenTheOnesBeforeAndAfter()
    {
        Segment[] segments = [Paragraph(0, "First."), Paragraph(1, "Second."), Paragraph(2, "Third.")];

        SegmentHints.Around(segments, 1).Should().Be("First.\n\nThird.");
    }

    [Fact]
    public void Around_FirstAndLastParagraph_HaveOneNeighbour()
    {
        Segment[] segments = [Paragraph(0, "First."), Paragraph(1, "Second."), Paragraph(2, "Third.")];

        SegmentHints.Around(segments, 0).Should().Be("Second.");
        SegmentHints.Around(segments, 2).Should().Be("Second.");
    }

    [Fact]
    public void Around_OnlyParagraph_IsNull()
    {
        SegmentHints.Around([Paragraph(0, "Only.")], 0).Should().BeNull();
    }

    [Fact]
    public void Around_CodeInANeighbour_IsWrittenAsItIs()
    {
        Segment[] segments =
        [
            Paragraph(0, new TextRun { Text = "Returns " }, new CodeSpan { Text = "null" }, new TextRun { Text = " if none." }),
            Paragraph(1, "Second."),
        ];

        SegmentHints.Around(segments, 1).Should().Be("Returns null if none.");
    }

    [Fact]
    public void Around_LongNeighbour_IsCutAtAWord()
    {
        string sentence = string.Concat(Enumerable.Repeat("word ", 200));
        Segment[] segments = [Paragraph(0, sentence), Paragraph(1, "Second.")];

        string context = SegmentHints.Around(segments, 1)!;

        context.Length.Should().BeLessThanOrEqualTo(SegmentHints.MaxNeighbourLength + 1);
        context.Should().EndWith("word…");
    }
}