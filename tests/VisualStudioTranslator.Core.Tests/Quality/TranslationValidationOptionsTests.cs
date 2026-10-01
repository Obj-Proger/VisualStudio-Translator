using AwesomeAssertions;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Core.Quality;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Quality;

public sealed class TranslationValidationOptionsTests
{
    [Theory]
    [InlineData("ru", WritingScript.Cyrillic)]
    [InlineData("uk", WritingScript.Cyrillic)]
    [InlineData("sr", WritingScript.Cyrillic)]
    [InlineData("sr-Cyrl-RS", WritingScript.Cyrillic)]
    [InlineData("RU", WritingScript.Cyrillic)] // normalized first
    [InlineData("zh", WritingScript.Han)]
    [InlineData("zh-Hant-TW", WritingScript.Han)]
    [InlineData("zh_hans", WritingScript.Han)]
    [InlineData("ko", WritingScript.Hangul)]
    [InlineData("ar", WritingScript.Arabic)]
    [InlineData("he", WritingScript.Hebrew)]
    [InlineData("hi", WritingScript.Devanagari)]
    [InlineData("th", WritingScript.Thai)]
    [InlineData("el", WritingScript.Greek)]
    public void ForTarget_LanguageWithOneScript_ExpectsThatScript(string language, WritingScript expected)
    {
        TranslationValidationOptions.ForTarget(language).TargetScripts.Should().BeEquivalentTo([expected]);
    }

    [Fact]
    public void ForTarget_Japanese_ExpectsHanHiraganaAndKatakana()
    {
        TranslationValidationOptions.ForTarget("ja").TargetScripts
            .Should().BeEquivalentTo([WritingScript.Han, WritingScript.Hiragana, WritingScript.Katakana]);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("pt-BR")]
    [InlineData("sr-Latn")] // an explicit script overrides the language's usual one
    [InlineData("bn")] // a script WritingScript does not list
    [InlineData("xx-Beng")]
    [InlineData("english")] // invalid tag
    [InlineData("")]
    [InlineData(null)]
    public void ForTarget_LatinUnlistedOrInvalid_HasNoScriptCheck(string? language)
    {
        TranslationValidationOptions.ForTarget(language).TargetScripts.Should().BeEmpty();
    }

    [Fact]
    public void ForTarget_LeavesEveryOtherOptionAtItsDefault()
    {
        TranslationValidationOptions options = TranslationValidationOptions.ForTarget("ru");

        options.Should().BeEquivalentTo(new TranslationValidationOptions(), o => o.Excluding(x => x.TargetScripts));
    }

    [Fact]
    public void ForTarget_UsedWithTheValidator_FlagsAnUntranslatedSentence()
    {
        ProtectedSegment source = new()
        {
            Text = "Returns the number of items in the list.",
            Placeholders = new Dictionary<int, ProtectedPlaceholder>(),
        };

        TranslationValidationResult result = TranslationValidator.Validate(
            source, "Gives back how many entries the list has.", TranslationValidationOptions.ForTarget("ru"));

        result.Issues.Select(issue => issue.Kind).Should().Equal(ValidationIssueKind.WrongScript);
    }
}