using AwesomeAssertions;
using VisualStudioTranslator.Core.Quality;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Quality;

public sealed class ProtectedSegmentTests
{
    private static ProtectedSegment Segment(string text) =>
        new() { Text = text, Placeholders = new Dictionary<int, ProtectedPlaceholder>() };

    [Theory]
    [InlineData("Use ⟦0⟧ now", true)]
    [InlineData("⟦0:o⟧х⟦0:c⟧", true)] // a letter inside a wrapper, in any script
    [InlineData("⟦0⟧", false)]
    [InlineData("⟦0:o⟧⟦0:c⟧", false)] // the "o" and "c" of a marker are not text
    [InlineData("⟦0⟧ 123 - ;", false)]
    [InlineData("42", false)]
    [InlineData("", false)]
    public void ContainsTranslatableText_ReflectsLettersOutsidePlaceholders(string text, bool expected)
    {
        Segment(text).ContainsTranslatableText().Should().Be(expected);
    }
}