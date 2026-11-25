using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Tests.Support;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Localization;

// The extension's own words live in Strings.resx and its translations, read by UiText in the extension. Nothing in
// a build compares them, so these tests do: every language has every text, with the same placeholders, and UiText
// asks for nothing that is not there.
public sealed partial class UiTextResourcesTests
{
    private static readonly string Folder = Repository.PathOf(["src", "VisualStudioTranslator.Vsix", "Resources"]);

    public static TheoryData<string> Translations => [.. TargetLanguages.Choices.Where(tag => tag != "en")];

    [Fact]
    public void UiText_AsksForExactlyTheTextsOfTheEnglishFile()
    {
        string code = File.ReadAllText(Path.Combine(Folder, "UiText.cs"));
        IEnumerable<string> asked = Property().Matches(code).Select(match => match.Groups[1].Value);

        asked.Should().BeEquivalentTo(Read("Strings.resx").Keys);
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_HasEveryTextOfTheEnglishFileAndNoOther(string language)
    {
        Dictionary<string, string> english = Read("Strings.resx");

        Read($"Strings.{language}.resx").Keys.Should().BeEquivalentTo(english.Keys);
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_KeepsThePlaceholdersAndLeavesNothingEmpty(string language)
    {
        Dictionary<string, string> english = Read("Strings.resx");

        foreach ((string key, string text) in Read($"Strings.{language}.resx"))
        {
            text.Should().NotBeNullOrWhiteSpace(key);
            Placeholders(text).Should().BeEquivalentTo(Placeholders(english[key]), key);
        }
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_KeepsTheAccessKeyOfTheLanguageLabel(string language)
    {
        Read($"Strings.{language}.resx")["TranslateIntoLabel"].Should().Contain("_");
    }

    private static Dictionary<string, string> Read(string file) =>
        XDocument.Load(Path.Combine(Folder, file)).Root!.Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string?)data.Element("value") ?? string.Empty);

    private static IEnumerable<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(match => match.Value).Distinct();

    [GeneratedRegex(@"public static string (\w+) => Get\(\);")]
    private static partial Regex Property();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();
}