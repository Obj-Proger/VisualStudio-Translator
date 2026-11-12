using AwesomeAssertions;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Engine.LocalTranslation.Llama;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Llama;

public sealed class TranslationPromptTests
{
    private static string Build(string source, string target, string text = "Gets the name.") =>
        TranslationPrompt.Build(LanguagePair.Create(source, target)!, text);

    [Fact]
    public void Build_NamesBothLanguagesByNameAndCode()
    {
        Build("en", "ru").Should().Contain("English (en) to Russian (ru)");
    }

    [Fact]
    public void Build_AsksForTheTargetLanguage()
    {
        string prompt = Build("en", "de");

        prompt.Should().Contain("into German.");
        prompt.Should().Contain("Output only the German translation");
    }

    [Fact]
    public void Build_KeepsTheRegionOfALanguage()
    {
        Build("en", "pt-BR").Should().Contain("(pt-BR)");
    }

    [Fact]
    public void Build_LanguageTheSystemHasNoNameFor_UsesItsCode()
    {
        Build("en", "zz").Should().Contain("(zz)");
    }

    [Fact]
    public void Build_PutsTheTextLastAndUnchanged()
    {
        Build("en", "ru", "Use <g0>the cache</g0> with <x1/>.")
            .Should().EndWith("Text:\nUse <g0>the cache</g0> with <x1/>.");
    }

    [Fact]
    public void Build_SameInput_GivesTheSamePrompt()
    {
        Build("en", "ru").Should().Be(Build("en", "ru"));
    }
}