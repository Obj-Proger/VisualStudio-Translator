using AwesomeAssertions;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Engine.Translation;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.Translation;

public sealed class ProviderSelectorTests
{
    private static readonly LanguagePair EnglishToRussian = LanguagePair.Create("en", "ru")!;

    [Fact]
    public void Select_LocalAndCloudWithoutConsent_UsesTheLocalOneWhateverTheRegistrationOrder()
    {
        StubProvider cloud = new("cloud", ProviderKind.Cloud);
        StubProvider local = new("local");

        ProviderSelector.Select([cloud, local], EnglishToRussian, allowCloud: false)
            .Provider.Should().BeSameAs(local);
        ProviderSelector.Select([local, cloud], EnglishToRussian, allowCloud: false)
            .Provider.Should().BeSameAs(local);
    }

    [Fact]
    public void Select_LocalAndCloudWithConsent_PrefersTheCloudOneWhateverTheRegistrationOrder()
    {
        // Agreeing to cloud translation is asking for better quality.
        StubProvider cloud = new("cloud", ProviderKind.Cloud);
        StubProvider local = new("local");

        ProviderSelector.Select([local, cloud], EnglishToRussian, allowCloud: true)
            .Provider.Should().BeSameAs(cloud);
        ProviderSelector.Select([cloud, local], EnglishToRussian, allowCloud: true)
            .Provider.Should().BeSameAs(cloud);
    }

    [Fact]
    public void Select_WithConsentButTheCloudProviderCannotDoThePair_FallsBackToLocal()
    {
        StubProvider cloud = new("cloud", ProviderKind.Cloud) { SupportsPair = _ => false };
        StubProvider local = new("local");

        ProviderSelector.Select([cloud, local], EnglishToRussian, allowCloud: true)
            .Provider.Should().BeSameAs(local);
    }

    [Fact]
    public void Select_AmongLocalProviders_TheFirstRegisteredWins()
    {
        StubProvider first = new("first");
        StubProvider second = new("second");

        ProviderSelector.Select([first, second], EnglishToRussian, allowCloud: false)
            .Provider.Should().BeSameAs(first);
    }

    [Fact]
    public void Select_AmongCloudProvidersWithConsent_TheFirstRegisteredWins()
    {
        StubProvider first = new("first", ProviderKind.Cloud);
        StubProvider second = new("second", ProviderKind.Cloud);

        ProviderSelector.Select([first, second], EnglishToRussian, allowCloud: true)
            .Provider.Should().BeSameAs(first);
    }

    [Fact]
    public void Select_ProviderThatDoesNotSupportThePair_IsSkipped()
    {
        StubProvider unsupported = new("unsupported") { SupportsPair = _ => false };
        StubProvider supported = new("supported");

        ProviderSelector.Select([unsupported, supported], EnglishToRussian, allowCloud: false)
            .Provider.Should().BeSameAs(supported);
    }

    [Fact]
    public void Select_OnlyCloudCanDoIt_WithoutConsent_ReportsThatConsentIsRequired()
    {
        ProviderSelection selection = ProviderSelector.Select(
            [new StubProvider("cloud", ProviderKind.Cloud)], EnglishToRussian, allowCloud: false);

        selection.Provider.Should().BeNull();
        selection.CloudConsentRequired.Should().BeTrue();
    }

    [Fact]
    public void Select_OnlyCloudCanDoIt_WithConsent_SelectsIt()
    {
        StubProvider cloud = new("cloud", ProviderKind.Cloud);

        ProviderSelection selection = ProviderSelector.Select([cloud], EnglishToRussian, allowCloud: true);

        selection.Provider.Should().BeSameAs(cloud);
        selection.CloudConsentRequired.Should().BeFalse();
    }

    [Fact]
    public void Select_NothingSupportsThePair_SelectsNothingAndDoesNotAskForConsent()
    {
        StubProvider unsupportedCloud = new("cloud", ProviderKind.Cloud) { SupportsPair = _ => false };

        ProviderSelection selection = ProviderSelector.Select([unsupportedCloud], EnglishToRussian, allowCloud: false);

        selection.Provider.Should().BeNull();
        selection.CloudConsentRequired.Should().BeFalse();
    }

    [Fact]
    public void Select_NoProviders_SelectsNothing()
    {
        ProviderSelector.Select([], EnglishToRussian, allowCloud: true).Provider.Should().BeNull();
    }
}