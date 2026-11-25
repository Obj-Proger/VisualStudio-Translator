using AwesomeAssertions;
using VisualStudioTranslator.Core.Quality;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Quality;

public sealed class LanguageGuessTests
{
    [Theory]
    [InlineData("Возвращает количество элементов в ⟦0⟧.", "ru")]
    [InlineData("Возвращает IEnumerable элементов списка.", "ru-RU")]
    [InlineData("リストの要素数を返します。", "ja")]
    [InlineData("返回列表中的元素数。", "zh-Hans")]
    [InlineData("목록의 요소 수를 반환합니다.", "ko")]
    public void IsAlreadyIn_TextInTheLanguagesOwnScript_IsTrue(string text, string language)
    {
        LanguageGuess.IsAlreadyIn(text, language).Should().BeTrue();
    }

    [Theory]
    [InlineData("Gets the number of elements in ⟦0⟧.", "ru")]
    [InlineData("Возвращает количество элементов.", "ko")]
    [InlineData("목록의 요소 수를 반환합니다.", "ja")]
    public void IsAlreadyIn_TextInAnotherScript_IsFalse(string text, string language)
    {
        LanguageGuess.IsAlreadyIn(text, language).Should().BeFalse();
    }

    [Theory]
    [InlineData("Gets the number of elements.", "en")]
    [InlineData("Gibt die Anzahl der Elemente zurück.", "de")]
    public void IsAlreadyIn_LatinScriptLanguage_CannotBeTold(string text, string language)
    {
        LanguageGuess.IsAlreadyIn(text, language).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("⟦0⟧ ⟦1⟧ 42")]
    public void IsAlreadyIn_NoLetters_IsFalse(string text)
    {
        LanguageGuess.IsAlreadyIn(text, "ru").Should().BeFalse();
    }

    [Fact]
    public void IsAlreadyIn_EnglishQuotingAFewRussianWords_IsFalse()
    {
        LanguageGuess.IsAlreadyIn("Returns the localized label, such as \"Сохранить\", for the command.", "ru").Should().BeFalse();
    }

    [Fact]
    public void IsAlreadyIn_MostlyCodeWithAFewWords_IsFalse()
    {
        LanguageGuess.IsAlreadyIn("ConfigureAwait ContinueOnCapturedContext и SynchronizationContext", "ru").Should().BeFalse();
    }
}