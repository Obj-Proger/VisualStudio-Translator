using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using VisualStudioTranslator.Core.Caching;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Engine.Translation;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.Translation;

public sealed class RejectedTranslationsTests
{
    private static readonly ProviderInfo Local = new()
    {
        Id = "local",
        DisplayName = "Local",
        Revision = "model-a/prompt-2",
        Kind = ProviderKind.Local,
        QualityTier = QualityTiers.Compact,
    };

    private readonly FakeTimeProvider _time = new();
    private readonly RejectedTranslations _rejections;

    public RejectedTranslationsTests() => _rejections = new RejectedTranslations(_time);

    private static TranslationCacheKey Key(string text) => TranslationCacheKey.Create(new TranslationCacheKeyInputs
    {
        Languages = LanguagePair.Create("en", "ru")!,
        MarkupProtection = MarkupSupport.None,
        GlossaryFingerprint = string.Empty,
        Text = text,
    });

    [Fact]
    public void IsRejected_NothingRemembered_IsFalse()
    {
        _rejections.IsRejected(Key("Gets the name."), Local).Should().BeFalse();
    }

    [Fact]
    public void IsRejected_AfterRemember_IsTrueForThatTextAndProvider()
    {
        _rejections.Remember(Key("Gets the name."), Local);

        _rejections.IsRejected(Key("Gets the name."), Local).Should().BeTrue();
        _rejections.IsRejected(Key("Sets the name."), Local).Should().BeFalse();
    }

    [Fact]
    public void IsRejected_AnotherRevisionOfTheProvider_GetsAFreshChance()
    {
        _rejections.Remember(Key("Gets the name."), Local);

        _rejections.IsRejected(Key("Gets the name."), Local with { Revision = "model-b/prompt-2" }).Should().BeFalse();
    }

    [Fact]
    public void IsRejected_AnotherProvider_GetsAFreshChance()
    {
        _rejections.Remember(Key("Gets the name."), Local);

        _rejections.IsRejected(Key("Gets the name."), Local with { Id = "cloud" }).Should().BeFalse();
    }

    [Fact]
    public void IsRejected_OnceTheRetentionHasPassed_IsFalse()
    {
        _rejections.Remember(Key("Gets the name."), Local);

        _time.Advance(RejectedTranslations.Retention - TimeSpan.FromSeconds(1));
        _rejections.IsRejected(Key("Gets the name."), Local).Should().BeTrue();

        _time.Advance(TimeSpan.FromSeconds(1));
        _rejections.IsRejected(Key("Gets the name."), Local).Should().BeFalse();
    }

    [Fact]
    public void Remember_BeyondCapacity_KeepsTheNewestEntries()
    {
        for (int i = 0; i < 5000; i++)
        {
            _rejections.Remember(Key("Segment " + i), Local);
            _time.Advance(TimeSpan.FromMilliseconds(1));
        }

        _rejections.IsRejected(Key("Segment 4999"), Local).Should().BeTrue();
        _rejections.IsRejected(Key("Segment 0"), Local).Should().BeFalse();
    }
}