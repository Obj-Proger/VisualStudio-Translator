using AwesomeAssertions;
using VisualStudioTranslator.Core.Languages;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Languages;

public sealed class LanguagePairTests
{
    [Fact]
    public void Create_NormalizesBothTags()
    {
        LanguagePair? pair = LanguagePair.Create("EN", "pt_br");

        pair.Should().NotBeNull();
        pair!.Source.Should().Be("en");
        pair.Target.Should().Be("pt-BR");
    }

    [Fact]
    public void Create_DifferentSpellingsOfTheSameTags_ProduceEqualPairs()
    {
        // Equality is what makes a pair usable inside a cache key.
        LanguagePair.Create("EN", "ru").Should().Be(LanguagePair.Create("en", "RU"));
    }

    [Theory]
    [InlineData("en", null)]
    [InlineData(null, "ru")]
    [InlineData("en", "english")]
    [InlineData("", "ru")]
    public void Create_EitherTagInvalid_ReturnsNull(string? source, string? target)
    {
        LanguagePair.Create(source, target).Should().BeNull();
    }

    [Fact]
    public void ToString_ShowsDirection()
    {
        LanguagePair.Create("en", "pt_br")!.ToString().Should().Be("en->pt-BR");
    }
}