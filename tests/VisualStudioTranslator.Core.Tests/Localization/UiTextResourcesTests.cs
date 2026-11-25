using System.Text.Json;
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

    [Fact]
    public void SettingsRegistration_NamesOnlyTextsTheEnglishFileHas()
    {
        string registration = File.ReadAllText(
            Repository.PathOf(["src", "VisualStudioTranslator.Vsix", "Options", "registration.json"]));
        List<string> named = [.. RegistrationReference().Matches(registration).Select(match => match.Groups[1].Value)];

        named.Should().NotBeEmpty();
        named.Should().OnlyContain(name => Read("Strings.resx").ContainsKey(name));
    }

    [Fact]
    public void SettingsRegistration_TranslatesEveryTitleAndDescriptionButTheProductName()
    {
        using JsonDocument registration = SettingsRegistration.Read();
        List<string> literal = [];
        Collect(registration.RootElement, literal);

        literal.Should().Equal("Visual Studio Translator");

        static void Collect(JsonElement element, List<string> literal)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name is "title" or "description"
                    && property.Value.GetString() is { } text
                    && !text.StartsWith('@'))
                {
                    literal.Add(text);
                }

                Collect(property.Value, literal);
            }
        }
    }

    private static Dictionary<string, string> Read(string file) =>
        XDocument.Load(Path.Combine(Folder, file)).Root!.Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string?)data.Element("value") ?? string.Empty);

    private static IEnumerable<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(match => match.Value).Distinct();

    [GeneratedRegex(@"public static string (\w+) => Get\(\);")]
    private static partial Regex Property();

    [GeneratedRegex(@"""@VisualStudioTranslator\.Vsix\.Resources\.Strings:(\w+);\.\.\\\\VisualStudioTranslator\.Vsix\.dll""")]
    private static partial Regex RegistrationReference();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();
}