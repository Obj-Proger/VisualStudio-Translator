using AwesomeAssertions;
using VisualStudioTranslator.Core.Documentation;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Documentation;

public sealed class TooltipTextTests
{
    private static TooltipRun Text(string text) => new(text, IsCode: false);

    private static TooltipRun Code(string text) => new(text, IsCode: true);

    // "Both tags are in LanguageTag.Normalize(string) form." as Roslyn colours it.
    private static readonly IReadOnlyList<TooltipRun> Documentation =
    [
        Text("Both tags are in "), Code("LanguageTag"), Code("."), Code("Normalize"), Code("("), Code("string"), Code(")"),
        Text(" form."),
    ];

    [Fact]
    public void IsProse_Documentation_IsTrue()
    {
        TooltipText.IsProse(Documentation).Should().BeTrue();
    }

    [Theory]
    [InlineData("Returns:")]
    [InlineData(" ")]
    [InlineData("'value'")]
    public void IsProse_CaptionOrBlankOrQuotedName_IsFalse(string text)
    {
        TooltipText.IsProse([Text(text)]).Should().BeFalse();
    }

    [Fact]
    public void IsProse_Signature_IsFalse()
    {
        TooltipText.IsProse([Code("class"), Text(" "), Code("LanguagePair")]).Should().BeFalse();
    }

    [Fact]
    public void ToDocument_CodeRunsNextToEachOther_AreOneCodeSpan()
    {
        Paragraph paragraph = (Paragraph)TooltipText.ToDocument([Documentation]).Summary!.Blocks.Single();

        paragraph.Inlines.Should().HaveCount(3);
        paragraph.Inlines[1].Should().BeOfType<CodeSpan>().Which.Text.Should().Be("LanguageTag.Normalize(string)");
    }

    [Fact]
    public void ToDocument_SpaceBetweenCodeRuns_StaysInTheCodeSpan()
    {
        Paragraph paragraph = (Paragraph)TooltipText.ToDocument(
            [[Text("Call "), Code("Create"), Code("("), Code("string"), Text(", "), Code("string"), Code(")"), Text(" first.")]])
            .Summary!.Blocks.Single();

        paragraph.Inlines.OfType<CodeSpan>().Select(code => code.Text).Should().Equal("Create(string", "string)");
    }

    [Fact]
    public void ToDocument_QuotedNameInText_IsACodeSpan()
    {
        Paragraph paragraph = (Paragraph)TooltipText.ToDocument([[Text("The name 'count' does not exist.")]])
            .Summary!.Blocks.Single();

        paragraph.Inlines.OfType<CodeSpan>().Single().Text.Should().Be("'count'");
    }

    [Fact]
    public void ToDocument_WebAddressInText_IsACodeSpan()
    {
        Paragraph paragraph = (Paragraph)TooltipText.ToDocument(
            [[Text("Writes a message. [MDN Reference](https://developer.mozilla.org/docs/Web/API/console/log_static)")]])
            .Summary!.Blocks.Single();

        paragraph.Inlines.OfType<CodeSpan>().Single().Text
            .Should().Be("https://developer.mozilla.org/docs/Web/API/console/log_static");
    }

    [Fact]
    public void ToDocument_LineBreakInsideText_IsASpace()
    {
        Paragraph paragraph = (Paragraph)TooltipText.ToDocument([[Text("Values above the maximum become the\n  maximum.")]])
            .Summary!.Blocks.Single();

        paragraph.Inlines.Single().Should().BeOfType<TextRun>().Which.Text.Should().Be("Values above the maximum become the maximum.");
    }

    [Fact]
    public void ToDocument_EmptyLineInsideText_Stays()
    {
        Paragraph paragraph = (Paragraph)TooltipText.ToDocument([[Text("First part of it.\n\nSecond part of it.")]])
            .Summary!.Blocks.Single();

        paragraph.Inlines.Single().Should().BeOfType<TextRun>().Which.Text.Should().Be("First part of it.\n\nSecond part of it.");
    }

    [Fact]
    public void FromDocument_TranslatedLine_PointsBackAtTheOriginalCodeRuns()
    {
        DocumentModel translated = new()
        {
            Summary = new Section
            {
                Blocks =
                [
                    new Paragraph
                    {
                        Inlines =
                        [
                            new TextRun { Text = "Оба тега в форме " },
                            new CodeSpan { Text = "LanguageTag.Normalize(string)" },
                            new TextRun { Text = "." },
                        ],
                    },
                ],
            },
        };

        IReadOnlyList<TranslatedPiece> line = TooltipText.FromDocument([Documentation], translated).Single()!;

        line.Should().Equal(
            new TranslatedPiece("Оба тега в форме "),
            new TranslatedPiece("LanguageTag.Normalize(string)", FirstCodeRun: 1, CodeRunCount: 6),
            new TranslatedPiece("."));
    }

    [Fact]
    public void FromDocument_CodeTheTranslationMoved_StillFindsItsRuns()
    {
        IReadOnlyList<TooltipRun> line = [Code("Count"), Text(" is never negative, and "), Code("Length"), Text(" neither.")];
        DocumentModel translated = new()
        {
            Summary = new Section
            {
                Blocks =
                [
                    new Paragraph
                    {
                        Inlines =
                        [
                            new TextRun { Text = "Ни " },
                            new CodeSpan { Text = "Length" },
                            new TextRun { Text = ", ни " },
                            new CodeSpan { Text = "Count" },
                            new TextRun { Text = " не отрицательны." },
                        ],
                    },
                ],
            },
        };

        IReadOnlyList<TranslatedPiece> pieces = TooltipText.FromDocument([line], translated).Single()!;

        pieces.Where(piece => piece.IsCode).Select(piece => piece.FirstCodeRun).Should().Equal(2, 0);
    }

    [Fact]
    public void FromDocument_QuotedName_ComesBackAsText()
    {
        IReadOnlyList<TooltipRun> line = [Text("The name 'count' does not exist.")];
        DocumentModel document = TooltipText.ToDocument([line]);

        IReadOnlyList<TranslatedPiece> pieces = TooltipText.FromDocument([line], document).Single()!;

        pieces.Should().OnlyContain(piece => !piece.IsCode);
        string.Concat(pieces.Select(piece => piece.Text)).Should().Be("The name 'count' does not exist.");
    }

    [Fact]
    public void FromDocument_LineWithoutAParagraph_IsNull()
    {
        TooltipText.FromDocument([Documentation], new DocumentModel()).Should().ContainSingle().Which.Should().BeNull();
    }

    [Fact]
    public void ToDocument_SurvivesTheWriterAndTheParser()
    {
        DocumentModel document = TooltipText.ToDocument([Documentation, [Text("Second line of text.")]]);

        DocumentModel read = DocumentationXmlParser.Parse(DocumentationXmlWriter.Write(document));

        TooltipText.FromDocument([Documentation, [Text("Second line of text.")]], read)
            .Should().HaveCount(2).And.NotContainNulls();
    }
}