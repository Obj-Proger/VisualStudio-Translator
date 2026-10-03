using AwesomeAssertions;
using VisualStudioTranslator.Core.Quality;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Quality;

public sealed class PlaceholderArtifactsTests
{
    [Fact]
    public void Clean_NothingWrong_ReturnsTheTranslationUnchanged()
    {
        PlaceholderArtifacts.Clean("Falls back to ⟦0⟧ when none.", "Возвращается к ⟦0⟧, когда нет.")
            .Should().Be("Возвращается к ⟦0⟧, когда нет.");
    }

    [Fact]
    public void Clean_SecondClosingTokenBracket_IsRemoved()
    {
        PlaceholderArtifacts.Clean("Falls back to ⟦0⟧ when none.", "Возвращается к ⟦0⟧⟧ когда нет.")
            .Should().Be("Возвращается к ⟦0⟧ когда нет.");
    }

    [Fact]
    public void Clean_StrayOpeningTokenBracketAnywhere_IsRemoved()
    {
        PlaceholderArtifacts.Clean("Falls back to ⟦0⟧ when none.", "Возвращается ⟦ к ⟦0⟧ когда нет.")
            .Should().Be("Возвращается  к ⟦0⟧ когда нет.");
    }

    [Fact]
    public void Clean_SquareBracketAfterAPlaceholder_IsRemovedTogetherWithTheSpaceBeforeIt()
    {
        PlaceholderArtifacts.Clean("Falls back to ⟦0⟧ when none.", "Возвращается к ⟦0⟧ ], когда нет.")
            .Should().Be("Возвращается к ⟦0⟧, когда нет.");
    }

    [Fact]
    public void Clean_SquareBracketBeforeAPlaceholder_IsRemoved()
    {
        PlaceholderArtifacts.Clean("Falls back to ⟦0⟧ when none.", "Возвращается к [ ⟦0⟧ когда нет.")
            .Should().Be("Возвращается к ⟦0⟧ когда нет.");
    }

    [Fact]
    public void Clean_SquareBracketAfterAClosingWrapperMarker_IsRemoved()
    {
        PlaceholderArtifacts.Clean("Use ⟦0:o⟧the cache⟦0:c⟧ now.", "Используйте ⟦0:o⟧кэш⟦0:c⟧] сейчас.")
            .Should().Be("Используйте ⟦0:o⟧кэш⟦0:c⟧ сейчас.");
    }

    [Fact]
    public void Clean_BracketsTheSourceHadToo_AreKept()
    {
        // Equal counts: nothing is in excess, so the bracket next to the token is the author's own.
        PlaceholderArtifacts.Clean("Use ⟦0⟧[0] now.", "Используйте ⟦0⟧[0] сейчас.")
            .Should().Be("Используйте ⟦0⟧[0] сейчас.");
    }

    [Fact]
    public void Clean_ExcessBracketNotTouchingAPlaceholder_IsLeftAlone()
    {
        // Only a bracket beside a placeholder is a suspect; one in the middle of prose is not guessed at.
        PlaceholderArtifacts.Clean("Use ⟦0⟧ now.", "Используйте ⟦0⟧ сейчас ] вот.")
            .Should().Be("Используйте ⟦0⟧ сейчас ] вот.");
    }

    [Fact]
    public void Clean_SourceThatItselfContainsTokenBrackets_KeepsThemInTheTranslation()
    {
        PlaceholderArtifacts.Clean("The ⟧ character.", "Символ ⟧ здесь.").Should().Be("Символ ⟧ здесь.");
    }

    [Fact]
    public void Clean_TextWithoutPlaceholders_ReturnsItUnchanged()
    {
        PlaceholderArtifacts.Clean("Gets the value.", "Получает значение.").Should().Be("Получает значение.");
    }
}