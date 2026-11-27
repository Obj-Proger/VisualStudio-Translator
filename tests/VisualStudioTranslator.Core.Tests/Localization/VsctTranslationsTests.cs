using System.Xml.Linq;
using AwesomeAssertions;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Tests.Support;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Localization;

// The menu text is in Commands.vsct and a copy of it for each language, which the build puts in the satellite
// assemblies. Nothing in a build compares the copies with the original, so these tests do: a copy differs from it in
// its menu text alone, and the window's menu item reads as the window's caption in that language.
public sealed class VsctTranslationsTests
{
    private static readonly XNamespace Table = "http://schemas.microsoft.com/VisualStudio/2005-10-18/CommandTable";

    private static readonly string Folder = Repository.PathOf(["src", "VisualStudioTranslator.Vsix", "TranslatorWindow"]);

    private static readonly string Resources = Repository.PathOf(["src", "VisualStudioTranslator.Vsix", "Resources"]);

    public static TheoryData<string> Translations => [.. TargetLanguages.Choices.Where(tag => tag != "en")];

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_DiffersFromTheOriginalInItsMenuTextAlone(string language)
    {
        XDocument original = WithoutText(Path.Combine(Folder, "Commands.vsct"));
        XDocument translation = WithoutText(Path.Combine(Folder, $"Commands.{language}.vsct"));

        XNode.DeepEquals(original.Root, translation.Root).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_TranslatesEveryMenuText(string language)
    {
        XDocument translation = XDocument.Load(Path.Combine(Folder, $"Commands.{language}.vsct"));

        translation.Descendants(Table + "ButtonText").Should().OnlyContain(text => !string.IsNullOrWhiteSpace(text.Value));
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_NamesTheWindowAsItsCaptionDoes(string language)
    {
        XDocument translation = XDocument.Load(Path.Combine(Folder, $"Commands.{language}.vsct"));
        string caption = XDocument.Load(Path.Combine(Resources, $"Strings.{language}.resx")).Root!
            .Elements("data").Single(data => (string?)data.Attribute("name") == "WindowCaption").Element("value")!.Value;

        translation.Descendants(Table + "Button")
            .Single(button => (string?)button.Attribute("id") == "ShowTranslator")
            .Descendants(Table + "ButtonText").Single().Value
            .Should().Be(caption);
    }

    [Fact]
    public void EveryTranslation_IsBuilt()
    {
        string project = File.ReadAllText(
            Repository.PathOf(["src", "VisualStudioTranslator.Vsix", "VisualStudioTranslator.Vsix.csproj"]));

        foreach (string language in TargetLanguages.Choices.Where(tag => tag != "en"))
        {
            project.Should().Contain($@"<VSCTCompile Include=""TranslatorWindow\Commands.{language}.vsct"">");
        }
    }

    // The table without its comments and with every menu text emptied.
    private static XDocument WithoutText(string path)
    {
        XDocument document = XDocument.Load(path);

        document.DescendantNodes().OfType<XComment>().ToList().ForEach(comment => comment.Remove());

        foreach (XElement text in document.Descendants(Table + "ButtonText"))
        {
            text.Value = string.Empty;
        }

        return document;
    }
}