using AwesomeAssertions;
using VisualStudioTranslator.Core.Languages;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Languages;

public sealed class LanguageTagTests
{
    [Theory]
    [InlineData("en", "en")]
    [InlineData("EN", "en")]
    [InlineData(" ru ", "ru")]
    [InlineData("fil", "fil")]
    [InlineData("pt-br", "pt-BR")]
    [InlineData("pt_BR", "pt-BR")]
    [InlineData("zh-hans", "zh-Hans")]
    [InlineData("ZH-HANS-CN", "zh-Hans-CN")]
    [InlineData("sr-latn-rs", "sr-Latn-RS")]
    [InlineData("es-419", "es-419")]
    [InlineData("en-us-x-private", "en-US-x-private")]
    public void Normalize_ValidTag_ReturnsCanonicalCasing(string tag, string expected)
    {
        LanguageTag.Normalize(tag).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("e")] // primary language too short
    [InlineData("english")] // primary language too long
    [InlineData("12")] // primary language must be letters
    [InlineData("en-")] // empty subtag
    [InlineData("-en")]
    [InlineData("en--US")]
    [InlineData("en-US!")] // not alphanumeric
    [InlineData("русский")] // non-ASCII
    [InlineData("en-toolongsubtag")] // subtag over eight characters
    public void Normalize_InvalidTag_ReturnsNull(string? tag)
    {
        LanguageTag.Normalize(tag).Should().BeNull();
    }
}