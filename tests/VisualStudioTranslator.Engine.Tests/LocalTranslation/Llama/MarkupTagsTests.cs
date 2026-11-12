using AwesomeAssertions;
using VisualStudioTranslator.Engine.LocalTranslation.Llama;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Llama;

public sealed class MarkupTagsTests
{
    [Fact]
    public void ToTags_WritesAStandAloneTokenAsASelfClosingTag()
    {
        MarkupTags.ToTags("Returns the number of items in ⟦0⟧.").Should().Be("Returns the number of items in <x0/>.");
    }

    [Fact]
    public void ToTags_WritesAPairAsAnOpeningAndAClosingTag()
    {
        MarkupTags.ToTags("Use ⟦0:o⟧the cache⟦0:c⟧ to avoid calls to ⟦1⟧.")
            .Should().Be("Use <g0>the cache</g0> to avoid calls to <x1/>.");
    }

    [Fact]
    public void ToTags_KeepsNumbersOfSeveralDigits()
    {
        MarkupTags.ToTags("⟦12⟧ and ⟦345:o⟧x⟦345:c⟧").Should().Be("<x12/> and <g345>x</g345>");
    }

    [Fact]
    public void ToTags_LeavesTextWithoutTokensAlone()
    {
        MarkupTags.ToTags("Gets the name of a List<T>.").Should().Be("Gets the name of a List<T>.");
    }

    [Theory]
    [InlineData("Returns ⟦0⟧.")]
    [InlineData("⟦0:o⟧Important:⟦0:c⟧ calling ⟦1⟧ twice has no effect.")]
    [InlineData("See ⟦0:o⟧the documentation⟦0:c⟧ on how ⟦1⟧ handles ⟦2:o⟧cancellation⟦2:c⟧.")]
    [InlineData("No tokens at all.")]
    public void ToTokens_UndoesToTags(string text)
    {
        MarkupTags.ToTokens(MarkupTags.ToTags(text)).Should().Be(text);
    }

    [Fact]
    public void ToTokens_AcceptsASpaceBeforeTheClosingSlash()
    {
        MarkupTags.ToTokens("Возвращает <x0 />.").Should().Be("Возвращает ⟦0⟧.");
    }

    [Theory]
    [InlineData("<x0>")]
    [InlineData("</x0>")]
    [InlineData("<g0/>")]
    [InlineData("<y0/>")]
    [InlineData("<x/>")]
    [InlineData("< x0/>")]
    [InlineData("<X0/>")]
    public void ToTokens_LeavesATagInAnyOtherShapeAsText(string text)
    {
        MarkupTags.ToTokens(text).Should().Be(text);
    }

    [Theory]
    [InlineData("Write <x1/> to the stream.")]
    [InlineData("Wrap it in <g2> for emphasis.")]
    [InlineData("Close it with </g3>.")]
    [InlineData("Spaced like < x1 />.")]
    public void CanCarry_TextThatAlreadyLooksLikeATag_IsFalse(string text)
    {
        MarkupTags.CanCarry(text).Should().BeFalse();
    }

    [Theory]
    [InlineData("Gets the name of a List<T>.")]
    [InlineData("True when a < b and b > c.")]
    [InlineData("Use ⟦0:o⟧the cache⟦0:c⟧ with ⟦1⟧.")]
    [InlineData("See <see/> and <x>.")]
    public void CanCarry_OrdinaryText_IsTrue(string text)
    {
        MarkupTags.CanCarry(text).Should().BeTrue();
    }
}