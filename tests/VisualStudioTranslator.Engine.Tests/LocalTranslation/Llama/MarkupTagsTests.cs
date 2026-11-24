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
        string sent = MarkupTags.ToTags(text);

        MarkupTags.ToTokens(sent, sent).Should().Be(text);
    }

    [Fact]
    public void ToTokens_AcceptsASpaceBeforeTheClosingSlash()
    {
        MarkupTags.ToTokens("Возвращает <x0 />.", "Returns <x0/>.").Should().Be("Возвращает ⟦0⟧.");
    }

    [Fact]
    public void ToTokens_PairTheTextDidNotHave_IsDroppedAndItsWordsKept()
    {
        // What Gemma 4 did with a quoted phrase in a summary that had no markup at all.
        string answer = "Позволяет отличить <g0>«я больше никому не нужен»</g0> от <g1>«давно ничего не просили»</g1>.";

        MarkupTags.ToTokens(answer, "Tells \"nobody needs me\" from \"nobody has asked lately\".")
            .Should().Be("Позволяет отличить «я больше никому не нужен» от «давно ничего не просили».");
    }

    [Fact]
    public void ToTokens_PairWithAnotherNumberThanTheOneSent_IsDropped()
    {
        MarkupTags.ToTokens("Используйте <g0>кэш</g0> и <g1>пул</g1>.", "Use <g0>the cache</g0> and the pool.")
            .Should().Be("Используйте ⟦0:o⟧кэш⟦0:c⟧ и пул.");
    }

    [Theory]
    [InlineData("Возвращает <x3/> имя.", "Возвращает имя.")]
    [InlineData("Возвращает имя <x3/>.", "Возвращает имя.")]
    [InlineData("<x3/> Возвращает имя.", " Возвращает имя.")]
    [InlineData("Возвращает<x3/>имя.", "Возвращаетимя.")]
    public void ToTokens_StandAloneTagTheTextDidNotHave_IsDroppedWithTheSpaceBeforeIt(string answer, string expected)
    {
        MarkupTags.ToTokens(answer, "Returns the name.").Should().Be(expected);
    }

    [Fact]
    public void ToTokens_KnownAndInventedTagsTogether_KeepsOnlyTheKnown()
    {
        MarkupTags.ToTokens("Записывает <x0/> в <g5>поток</g5>.", "Writes <x0/> to the stream.")
            .Should().Be("Записывает ⟦0⟧ в поток.");
    }

    [Theory]
    [InlineData("Returns the name.", false)]
    [InlineData("Gets the name of a List<T>.", false)]
    [InlineData("Returns <x0/>.", true)]
    [InlineData("Use <g1>the cache</g1>.", true)]
    public void HasTags_TellsWhetherTheTextHoldsTags(string text, bool expected)
    {
        MarkupTags.HasTags(text).Should().Be(expected);
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
        MarkupTags.ToTokens(text, "<x0/> and <g0>a phrase</g0>").Should().Be(text);
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