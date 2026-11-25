using System.Runtime.CompilerServices;
using System.Text.Json;
using AwesomeAssertions;
using VisualStudioTranslator.Core.Languages;
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

    [Fact]
    public void SettingsRegistration_OffersExactlyTheChoicesInTheirOrder()
    {
        using JsonDocument registration = ReadRegistration();
        JsonElement language = registration.RootElement.GetProperty("properties").GetProperty(LanguageMoniker);

        language.GetProperty("default").GetString().Should().Be(TargetLanguages.FollowVisualStudio);
        language.GetProperty("enum").EnumerateArray().Select(value => value.GetString())
            .Should().Equal([TargetLanguages.FollowVisualStudio, .. TargetLanguages.Choices]);
        language.GetProperty("enumItemLabels").GetArrayLength().Should().Be(TargetLanguages.Choices.Count + 1);
    }

    // The extension's settings are declared in a file Visual Studio reads; a test here keeps it in step with
    // the code, which no build would otherwise notice.
    private static JsonDocument ReadRegistration([CallerFilePath] string thisFile = "")
    {
        DirectoryInfo? directory = new FileInfo(thisFile).Directory;

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VisualStudio-Translator.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test runs from within the repository");

        string path = Path.Combine(directory!.FullName, "src", "VisualStudioTranslator.Vsix", "Options", "registration.json");

        return JsonDocument.Parse(File.ReadAllText(path));
    }
}