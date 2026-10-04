using AwesomeAssertions;
using VisualStudioTranslator.Core.Caching;
using VisualStudioTranslator.Core.Providers.Abstractions;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Caching;

public sealed class CachedTranslationTests
{
    private static ProviderInfo Provider(string id, string revision, int tier) => new()
    {
        Id = id,
        DisplayName = id,
        Revision = revision,
        Kind = tier >= QualityTiers.Cloud ? ProviderKind.Cloud : ProviderKind.Local,
        QualityTier = tier,
    };

    private static CachedTranslation Entry(string id, string revision, int tier) => new()
    {
        Text = "text",
        ProviderId = id,
        ProviderRevision = revision,
        QualityTier = tier,
    };

    private static readonly ProviderInfo Local = Provider("local", "r1", QualityTiers.Compact);
    private static readonly ProviderInfo LocalNewerModel = Provider("local", "r2", QualityTiers.Compact);
    private static readonly ProviderInfo Azure = Provider("azure", "v3", QualityTiers.Cloud);
    private static readonly ProviderInfo DeepL = Provider("deepl", "v2", QualityTiers.Cloud);

    [Fact]
    public void From_CopiesTheProvidersIdentityAndTier()
    {
        CachedTranslation entry = CachedTranslation.From(Azure, "перевод");

        entry.Text.Should().Be("перевод");
        entry.ProviderId.Should().Be("azure");
        entry.ProviderRevision.Should().Be("v3");
        entry.QualityTier.Should().Be(QualityTiers.Cloud);
    }

    // --- Satisfies ---

    [Fact]
    public void Satisfies_ATranslationBySameProviderAndRevision_IsServed()
    {
        Entry("local", "r1", QualityTiers.Compact).Satisfies(Local).Should().BeTrue();
    }

    [Fact]
    public void Satisfies_ATranslationByAnOlderRevisionOfTheModel_IsStale()
    {
        Entry("local", "r1", QualityTiers.Compact).Satisfies(LocalNewerModel).Should().BeFalse();
    }

    [Fact]
    public void Satisfies_ALocalTranslation_NeverServesACloudRequest()
    {
        Entry("local", "r1", QualityTiers.Compact).Satisfies(Azure).Should().BeFalse();
    }

    [Fact]
    public void Satisfies_ACloudTranslation_ServesALocalRequest()
    {
        // The better translation stays in use when the cloud is switched off.
        Entry("azure", "v3", QualityTiers.Cloud).Satisfies(Local).Should().BeTrue();
    }

    [Fact]
    public void Satisfies_ACloudTranslation_ServesTheSameCloudProvider()
    {
        Entry("azure", "v3", QualityTiers.Cloud).Satisfies(Azure).Should().BeTrue();
    }

    [Fact]
    public void Satisfies_ACloudTranslationByAnotherCloudProvider_DoesNotServe()
    {
        // Same tier, different provider: the user chose this one, so it is asked.
        Entry("azure", "v3", QualityTiers.Cloud).Satisfies(DeepL).Should().BeFalse();
    }

    // --- ShouldReplace ---

    [Fact]
    public void ShouldReplace_EmptySlot_AlwaysTrue()
    {
        CachedTranslation.ShouldReplace(null, Entry("local", "r1", QualityTiers.Compact)).Should().BeTrue();
    }

    [Fact]
    public void ShouldReplace_CloudOverLocal_True()
    {
        CachedTranslation.ShouldReplace(
            Entry("local", "r1", QualityTiers.Compact), Entry("azure", "v3", QualityTiers.Cloud)).Should().BeTrue();
    }

    [Fact]
    public void ShouldReplace_LocalOverCloud_False()
    {
        CachedTranslation.ShouldReplace(
            Entry("azure", "v3", QualityTiers.Cloud), Entry("local", "r1", QualityTiers.Compact)).Should().BeFalse();
    }

    [Fact]
    public void ShouldReplace_NewerRevisionOfTheSameTier_True()
    {
        CachedTranslation.ShouldReplace(
            Entry("local", "r1", QualityTiers.Compact), Entry("local", "r2", QualityTiers.Compact)).Should().BeTrue();
    }

    [Fact]
    public void ShouldReplace_AnotherCloudProviderOfTheSameTier_True()
    {
        CachedTranslation.ShouldReplace(
            Entry("azure", "v3", QualityTiers.Cloud), Entry("deepl", "v2", QualityTiers.Cloud)).Should().BeTrue();
    }
}