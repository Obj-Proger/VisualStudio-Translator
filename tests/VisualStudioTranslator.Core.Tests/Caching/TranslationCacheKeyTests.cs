using AwesomeAssertions;
using VisualStudioTranslator.Core.Caching;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Caching;

public sealed class TranslationCacheKeyTests
{
    private static TranslationCacheKeyInputs Baseline => new()
    {
        Languages = LanguagePair.Create("en", "ru")!,
        MarkupProtection = MarkupSupport.None,
        GlossaryFingerprint = "glossary-fp",
        Text = "Returns the number of items ⟦0⟧.",
    };

    [Fact]
    public void Create_SameInputs_ProduceEqualKeys()
    {
        TranslationCacheKey.Create(Baseline).Should().Be(TranslationCacheKey.Create(Baseline));
    }

    [Fact]
    public void Create_Value_IsLowercaseHexSha256()
    {
        TranslationCacheKey.Create(Baseline).Value.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Create_ChangingAnyInput_ChangesTheKey()
    {
        TranslationCacheKeyInputs baseline = Baseline;

        TranslationCacheKeyInputs[] variants =
        [
            baseline,
            baseline with { Languages = LanguagePair.Create("en", "de")! },
            baseline with { Languages = LanguagePair.Create("fr", "ru")! },
            baseline with { MarkupProtection = MarkupSupport.Html },
            baseline with { GlossaryFingerprint = "other-fp" },
            baseline with { Text = "Returns the number of items ⟦1⟧." },
        ];

        variants.Select(variant => TranslationCacheKey.Create(variant).Value)
            .Distinct()
            .Should().HaveCount(variants.Length);
    }

    [Fact]
    public void Create_FieldBoundaries_AreUnambiguous()
    {
        // The same characters split differently between two fields must not collide.
        TranslationCacheKeyInputs first = Baseline with { GlossaryFingerprint = "a", Text = "bc" };
        TranslationCacheKeyInputs second = Baseline with { GlossaryFingerprint = "ab", Text = "c" };

        TranslationCacheKey.Create(first).Should().NotBe(TranslationCacheKey.Create(second));
    }

    [Fact]
    public void Create_TextWithLoneSurrogate_DoesNotThrow()
    {
        Func<TranslationCacheKey> act = () => TranslationCacheKey.Create(Baseline with { Text = "broken\uD800" });

        act.Should().NotThrow().Which.Value.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Create_KnownInputs_MatchesPinnedValue()
    {
        // Pins the key derivation. If this fails, the way keys are built has changed and
        // every cache on every machine would silently stop matching: raise
        // CacheVersions.Schema together with updating this value, deliberately.
        TranslationCacheKey.Create(Baseline).Value
            .Should().Be("77165dc652eda0d48b56f898c2d34e4db77db7838121a9957a7a5e7326193b0a");
    }
}