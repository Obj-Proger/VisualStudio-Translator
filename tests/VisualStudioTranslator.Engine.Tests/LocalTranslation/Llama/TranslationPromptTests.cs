using AwesomeAssertions;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Engine.LocalTranslation.Llama;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Llama;

public sealed class TranslationPromptTests
{
    private static readonly LanguagePair EnglishToRussian = LanguagePair.Create("en", "ru")!;

    private static string Build(string source, string target, string text = "Gets the name.") =>
        TranslationPrompt.Build(LanguagePair.Create(source, target)!, text);

    [Fact]
    public void Build_NamesBothLanguagesByNameAndCode()
    {
        Build("en", "ru").Should().Contain("English (en) to Russian (ru)");
    }

    [Fact]
    public void Build_SourceUnknown_NamesOnlyTheTargetAndLeavesTextAlreadyInIt()
    {
        string prompt = Build(LanguageTag.Undetermined, "ru");

        prompt.Should().Contain("translator of software documentation into Russian (ru)")
            .And.Contain("If the text is already written in Russian, output it unchanged.")
            .And.NotContain("und");
    }

    [Fact]
    public void Build_AsksForProgrammingTermsAndKeepsNamesAndSubjects()
    {
        Build("en", "ru").Should().Contain("in their programming sense")
            .And.Contain("Keep the names of products and components")
            .And.Contain("Write natural, grammatical Russian");
    }

    [Fact]
    public void Build_SourceKnown_HasNoRuleForTextAlreadyInTheTarget()
    {
        Build("en", "ru").Should().NotContain("already written");
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
    public void Build_TextWithTags_ExplainsTheTags()
    {
        Build("en", "ru", "Returns <x0/>.").Should().Contain("Copy every tag into the translation unchanged");
    }

    [Fact]
    public void Build_TextWithoutTags_DoesNotMentionTags()
    {
        // Told about tags with none in the text, the model put some of its own around quoted phrases.
        Build("en", "ru", "Tells \"nobody needs me\" from \"nobody has asked\".").Should().NotContain("<x0/>")
            .And.NotContain("tag");
    }

    [Fact]
    public void Build_SameInput_GivesTheSamePrompt()
    {
        Build("en", "ru").Should().Be(Build("en", "ru"));
    }

    [Fact]
    public void Build_Codes_AreNamedUnderTheirTagsBeforeTheText()
    {
        string prompt = TranslationPrompt.Build(
            EnglishToRussian,
            "Ensures a copy of <x0/> exists in <x1/>.",
            new Dictionary<int, string> { [0] = "sourceFolder", [1] = "deploymentsRoot" });

        prompt.Should().Contain("Tags:\n<x0/> = sourceFolder\n<x1/> = deploymentsRoot\n\nText:\n")
            .And.Contain("write the tag itself, not the code");
    }

    [Fact]
    public void Build_CodeOfATagTheTextDoesNotHold_IsLeftOut()
    {
        TranslationPrompt.Build(EnglishToRussian, "Returns <x1/>.", new Dictionary<int, string> { [0] = "a", [1] = "count" })
            .Should().Contain("<x1/> = count").And.NotContain("<x0/> = a");
    }

    [Fact]
    public void Build_LongCode_IsCut()
    {
        string code = new('a', 200);

        TranslationPrompt.Build(EnglishToRussian, "Returns <x0/>.", new Dictionary<int, string> { [0] = code })
            .Should().Contain("<x0/> = " + new string('a', 80) + "…\n").And.NotContain(new string('a', 81));
    }

    [Fact]
    public void Build_NoCodes_HasNoTagList()
    {
        Build("en", "ru", "Returns <x0/>.").Should().NotContain("Tags:");
    }

    [Fact]
    public void Build_Context_IsGivenBeforeTheTextNotToBeTranslated()
    {
        string prompt = TranslationPrompt.Build(EnglishToRussian, "Gets the name.", context: "The person to greet.");

        prompt.Should().Contain("Context:\nThe person to greet.\nEnd of context.\n\nText:\nGets the name.")
            .And.Contain("do not translate them");
    }

    [Fact]
    public void Build_NoContext_DoesNotMentionIt()
    {
        Build("en", "ru").Should().NotContain("Context:").And.NotContain("End of context");
    }
}