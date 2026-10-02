using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VisualStudioTranslator.Core.Caching;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Core.Quality;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.Caching;
using VisualStudioTranslator.Engine.Rpc;
using VisualStudioTranslator.Engine.Tests.Translation;
using VisualStudioTranslator.Engine.Translation;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.Rpc;

public sealed class TranslatorServiceTests
{
    private const string Sentence = "Returns the number of items.";
    private const string Input = "<member name=\"M:X\"><summary>" + Sentence + "</summary></member>";
    private const string OriginalOutput = "<member><summary><para>" + Sentence + "</para></summary></member>";
    private const string TranslatedOutput = "<member><summary><para>Возвращает количество элементов.</para></summary></member>";

    private static readonly Dictionary<string, string> Translations = new()
    {
        [Sentence] = "Возвращает количество элементов.",
    };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static TranslatorService Service(params ITranslationProvider[] providers) => new(
        NullLogger<TranslatorService>.Instance,
        new TranslationOrchestrator(new MemoryTranslationCache(), NullLogger<TranslationOrchestrator>.Instance),
        providers);

    private static TranslateDocumentationRequest Request(string target = "ru") => new()
    {
        DocumentationXml = Input,
        SourceLanguage = "en",
        TargetLanguage = target,
    };

    [Fact]
    public async Task TranslateDocumentationAsync_TranslatesAndReturnsParseableXml()
    {
        TranslateDocumentationResult result = await Service(new StubProvider("local", translations: Translations))
            .TranslateDocumentationAsync(Request(), Token);

        result.Outcome.Should().Be(TranslationOutcome.Completed);
        result.DocumentationXml.Should().Be(TranslatedOutput);
        result.SegmentCount.Should().Be(1);
        result.TranslatedCount.Should().Be(1);
        result.UnchangedCount.Should().Be(0);
        result.ProviderFailure.Should().BeNull();
    }

    [Fact]
    public async Task TranslateDocumentationAsync_InvalidTargetLanguage_ReturnsTheDocumentUnchanged()
    {
        StubProvider provider = new("local", translations: Translations);

        TranslateDocumentationResult result = await Service(provider)
            .TranslateDocumentationAsync(Request(target: "english"), Token);

        result.Outcome.Should().Be(TranslationOutcome.InvalidLanguage);
        result.DocumentationXml.Should().Be(OriginalOutput);
        result.UnchangedCount.Should().Be(1);
        provider.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TranslateDocumentationAsync_NoProviders_ReportsNoProviderAvailable()
    {
        TranslateDocumentationResult result = await Service().TranslateDocumentationAsync(Request(), Token);

        result.Outcome.Should().Be(TranslationOutcome.NoProviderAvailable);
        result.DocumentationXml.Should().Be(OriginalOutput);
    }

    [Fact]
    public async Task TranslateDocumentationAsync_ProviderThatDoesNotSupportThePair_ReportsNoProviderAvailable()
    {
        StubProvider provider = new("local", translations: Translations) { SupportsPair = _ => false };

        TranslateDocumentationResult result = await Service(provider).TranslateDocumentationAsync(Request(), Token);

        result.Outcome.Should().Be(TranslationOutcome.NoProviderAvailable);
        provider.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TranslateDocumentationAsync_OnlyACloudProviderAndNoConsent_AsksForConsentAndSendsNothing()
    {
        StubProvider cloud = new("cloud", ProviderKind.Cloud, Translations);

        TranslateDocumentationResult result = await Service(cloud).TranslateDocumentationAsync(Request(), Token);

        result.Outcome.Should().Be(TranslationOutcome.CloudConsentRequired);
        result.DocumentationXml.Should().Be(OriginalOutput);
        cloud.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TranslateDocumentationAsync_CloudProviderWithConsent_Translates()
    {
        StubProvider cloud = new("cloud", ProviderKind.Cloud, Translations);

        TranslateDocumentationResult result = await Service(cloud)
            .TranslateDocumentationAsync(Request() with { AllowCloudProvider = true }, Token);

        result.Outcome.Should().Be(TranslationOutcome.Completed);
        result.DocumentationXml.Should().Be(TranslatedOutput);
    }

    [Fact]
    public async Task TranslateDocumentationAsync_LocalAndCloudProviders_UsesTheLocalOne()
    {
        StubProvider cloud = new("cloud", ProviderKind.Cloud, Translations);
        StubProvider local = new("local", translations: Translations);

        await Service(cloud, local).TranslateDocumentationAsync(Request() with { AllowCloudProvider = true }, Token);

        cloud.Calls.Should().BeEmpty();
        local.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task TranslateDocumentationAsync_ProviderFailure_ReportsItAndKeepsTheOriginal()
    {
        StubProvider provider = new("local", translations: Translations)
        {
            Throws = new TranslationProviderException(ProviderFailureKind.QuotaExceeded, "limit reached"),
        };

        TranslateDocumentationResult result = await Service(provider).TranslateDocumentationAsync(Request(), Token);

        result.Outcome.Should().Be(TranslationOutcome.ProviderFailed);
        result.ProviderFailure.Should().Be(ProviderFailureKind.QuotaExceeded);
        result.DocumentationXml.Should().Be(OriginalOutput);
        result.UnchangedCount.Should().Be(1);
    }

    [Fact]
    public async Task TranslateDocumentationAsync_MalformedXml_IsAnEmptyDocumentNotAnError()
    {
        StubProvider provider = new("local", translations: Translations);
        TranslateDocumentationRequest request = Request() with { DocumentationXml = "<member><summary>unclosed" };

        TranslateDocumentationResult result = await Service(provider).TranslateDocumentationAsync(request, Token);

        result.Outcome.Should().Be(TranslationOutcome.Completed);
        result.SegmentCount.Should().Be(0);
        result.DocumentationXml.Should().Be("<member />");
        provider.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TranslateDocumentationAsync_GlossaryEntryWithBlankTerm_IsIgnored()
    {
        // A blank term, if it reached the glossary pattern, would match every space and
        // change the text sent to the provider.
        TranslateDocumentationRequest request = Request() with
        {
            Glossary = [new GlossaryEntry { Term = "  ", Kind = GlossaryEntryKind.DoNotTranslate }],
        };

        TranslateDocumentationResult result = await Service(new StubProvider("local", translations: Translations))
            .TranslateDocumentationAsync(request, Token);

        result.Outcome.Should().Be(TranslationOutcome.Completed);
        result.TranslatedCount.Should().Be(1);
    }

    [Fact]
    public async Task TranslateDocumentationAsync_CancelledToken_PropagatesTheCancellation()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> act = () => Service(new StubProvider("local", translations: Translations))
            .TranslateDocumentationAsync(Request(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}