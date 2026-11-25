using AwesomeAssertions;
using VisualStudioTranslator.Core.Documentation;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Documentation;

public sealed class PlainTextTests
{
    private static IReadOnlyList<string> Units(PlainTextDocument layout) =>
        [.. DocumentSegmenter.Segment(layout.Document).Select(segment => ((TextRun)segment.Inlines.Single()).Text)];

    // Translates every unit with the given function, the way the pipeline puts translations back.
    private static string RoundTrip(string text, Func<string, string> translate)
    {
        PlainTextDocument layout = PlainText.Parse(text);
        Dictionary<int, IReadOnlyList<Inline>> translations = [];

        foreach (Segment segment in DocumentSegmenter.Segment(layout.Document))
        {
            translations[segment.Id] = [new TextRun { Text = translate(((TextRun)segment.Inlines[0]).Text) }];
        }

        return PlainText.Write(layout, DocumentSegmenter.Compose(layout.Document, translations));
    }

    [Fact]
    public void Parse_LinesOfOneParagraph_AreOneUnit()
    {
        PlainTextDocument layout = PlainText.Parse("Gets the number of\n  elements in the list.");

        Units(layout).Should().Equal("Gets the number of elements in the list.");
    }

    [Fact]
    public void Parse_BlankLine_SeparatesParagraphs()
    {
        PlainTextDocument layout = PlainText.Parse("First paragraph.\r\n\r\n\r\nSecond paragraph.");

        Units(layout).Should().Equal("First paragraph.", "Second paragraph.");
        layout.Prefixes.Should().Equal("", "\n\n");
    }

    [Fact]
    public void Parse_ListItems_AreUnitsOfTheirOwnWithoutTheirMarkers()
    {
        PlainTextDocument layout = PlainText.Parse("Steps:\n- open the file\n- read it\n2. close it");

        Units(layout).Should().Equal("Steps:", "open the file", "read it", "close it");
        layout.Prefixes.Should().Equal("", "\n- ", "\n- ", "\n2. ");
    }

    [Fact]
    public void Parse_NothingButWhitespace_HasNoUnits()
    {
        PlainTextDocument layout = PlainText.Parse(" \n\t\n");

        layout.Document.Summary.Should().BeNull();
        PlainText.Write(layout, layout.Document).Should().BeEmpty();
    }

    [Fact]
    public void Parse_LongParagraph_IsCutBetweenSentencesWithinTheLimit()
    {
        string sentence = new string('a', 300) + ". ";
        string text = string.Concat(Enumerable.Repeat(sentence, 8)).TrimEnd();

        IReadOnlyList<string> units = Units(PlainText.Parse(text));

        units.Should().HaveCountGreaterThan(1).And.OnlyContain(unit => unit.Length <= PlainText.MaxUnitLength);
        units.Should().OnlyContain(unit => unit.EndsWith('.'));
        string.Join(" ", units).Should().Be(text);
    }

    [Fact]
    public void Parse_LongTextWithoutSentences_IsStillCutWithinTheLimit()
    {
        string text = string.Join(" ", Enumerable.Repeat("word", 600));

        IReadOnlyList<string> units = Units(PlainText.Parse(text));

        units.Should().OnlyContain(unit => unit.Length <= PlainText.MaxUnitLength);
        string.Join(" ", units).Should().Be(text);
    }

    [Fact]
    public void Write_PutsTheTranslationIntoTheOriginalLayout()
    {
        string translated = RoundTrip("Intro\nline.\n\n- one\n- two", unit => unit.ToUpperInvariant());

        translated.Should().Be("INTRO LINE.\n\n- ONE\n- TWO");
    }

    [Fact]
    public void Write_UnitLeftUntranslated_KeepsItsText()
    {
        PlainTextDocument layout = PlainText.Parse("Kept.");

        PlainText.Write(layout, layout.Document).Should().Be("Kept.");
    }
}