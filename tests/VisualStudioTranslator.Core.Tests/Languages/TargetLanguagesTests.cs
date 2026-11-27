using System.Text.Json;
using AwesomeAssertions;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Tests.Support;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Languages;

public sealed class TargetLanguagesTests
{
    private const string LanguageMoniker = "visualStudioTranslator.general.targetLanguage";

    [Theory]
    [InlineData("auto", "ru-RU", "ru-RU")]
    [InlineData("", "de-DE", "de-DE")]
    [InlineData(null, "fr-FR", "fr-FR")]
    [InlineData("ru", "en-US", "ru")]
    [InlineData("de", "ru-RU", "de")]
    public void Resolve_GivesTheChosenLanguageOrVisualStudiosOwn(string? choice, string display, string expected)
    {
        TargetLanguages.Resolve(choice, display).Should().Be(expected);
    }

    [Theory]
    [InlineData("auto", "en-US", "en-US")]
    [InlineData("en", "ru-RU", "en")]
    public void Resolve_English_IsALanguageLikeAnyOther(string choice, string display, string expected)
    {
        TargetLanguages.Resolve(choice, display).Should().Be(expected);
    }

    [Fact]
    public void Resolve_NoLanguageAtAll_IsNothingToTranslateInto()
    {
        TargetLanguages.Resolve("auto", "").Should().BeNull();
    }

    [Theory]
    [InlineData("en", "en-US", true)]
    [InlineData("pt-BR", "pt-PT", true)]
    [InlineData("zh-Hans", "zh", true)]
    [InlineData("en", "ru-RU", false)]
    [InlineData("en", "eu", false)]
    public void IsSameLanguage_ComparesTheLanguageNotTheRegion(string first, string second, bool expected)
    {
        TargetLanguages.IsSameLanguage(first, second).Should().Be(expected);
    }

    [Fact]
    public void Resolve_ChoiceFromAnotherVersionOfTheExtension_FollowsVisualStudio()
    {
        TargetLanguages.Resolve("tlh", "ru-RU").Should().Be("ru-RU");
    }

    [Fact]
    public void NativeName_IsTheLanguagesOwnName()
    {
        TargetLanguages.NativeName("de").Should().Be("Deutsch");
    }

    [Theory]
    [InlineData("ru", "Русский")]
    [InlineData("fr", "Français")]
    [InlineData("de", "Deutsch")]
    public void ListName_StartsWithACapital(string tag, string expected)
    {
        TargetLanguages.ListName(tag).Should().StartWith(expected);
    }

    [Fact]
    public void SettingsRegistration_OffersExactlyTheChoicesInTheirOrder()
    {
        using JsonDocument registration = SettingsRegistration.Read();
        JsonElement language = registration.Property(LanguageMoniker);

        language.GetProperty("default").GetString().Should().Be(TargetLanguages.FollowVisualStudio);
        language.GetProperty("enum").EnumerateArray().Select(value => value.GetString())
            .Should().Equal([TargetLanguages.FollowVisualStudio, .. TargetLanguages.Choices]);
        language.GetProperty("enumItemLabels").GetArrayLength().Should().Be(TargetLanguages.Choices.Count + 1);
    }
}