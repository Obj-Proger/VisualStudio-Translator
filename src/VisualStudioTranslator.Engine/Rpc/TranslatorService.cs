using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Core.Quality;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.LocalTranslation;
using VisualStudioTranslator.Engine.Translation;

namespace VisualStudioTranslator.Engine.Rpc;

/// <summary>
/// The Engine-side implementation of <see cref="ITranslatorService"/>, exposed to Vsix
/// clients over the named pipe RPC server.
/// </summary>
internal sealed class TranslatorService(
    ILogger<TranslatorService> logger,
    TranslationOrchestrator orchestrator,
    IEnumerable<ITranslationProvider> providers,
    IModelInstaller modelInstaller) : ITranslatorService
{
    public Task<ServiceInfo> HandshakeAsync(ClientInfo client, CancellationToken cancellationToken)
    {
        logger.Handshake(
            client.ProductName,
            client.VisualStudioVersion,
            client.ExtensionVersion,
            client.ProtocolMajor,
            client.ProtocolMinor);

        if (!ProtocolVersion.IsCompatibleWith(client.ProtocolMajor))
        {
            logger.IncompatibleClientProtocol(client.ProtocolMajor, ProtocolVersion.Major);
        }

        ServiceInfo info = new()
        {
            ServiceVersion = GetServiceVersion(),
            ProtocolMajor = ProtocolVersion.Major,
            ProtocolMinor = ProtocolVersion.Minor,
        };

        return Task.FromResult(info);
    }

    public async Task<TranslateDocumentationResult> TranslateDocumentationAsync(
        TranslateDocumentationRequest request, CancellationToken cancellationToken)
    {
        // The parser never throws, so an unreadable comment is just an empty document.
        DocumentModel document = DocumentationXmlParser.Parse(request.DocumentationXml);
        int segmentCount = DocumentSegmenter.Segment(document).Count;

        LanguagePair? languages = LanguagePair.Create(request.SourceLanguage, request.TargetLanguage);
        if (languages is null)
        {
            return Unchanged(document, segmentCount, TranslationOutcome.InvalidLanguage);
        }

        ProviderSelection selection = ProviderSelector.Select(providers, languages, request.AllowCloudProvider);
        if (selection.Provider is not { } provider)
        {
            if (selection.CloudConsentRequired)
            {
                return Unchanged(document, segmentCount, TranslationOutcome.CloudConsentRequired);
            }

            logger.NoProviderAvailable(languages);
            return Unchanged(document, segmentCount, TranslationOutcome.NoProviderAvailable);
        }

        // The wire is the one place a malformed entry can arrive. A blank term would match
        // everywhere once it became part of a pattern, so it never gets that far.
        Glossary glossary = new()
        {
            Entries = [.. (request.Glossary ?? []).Where(entry => !string.IsNullOrWhiteSpace(entry.Term))],
        };

        TranslationResult translation = await orchestrator.TranslateAsync(
            new TranslationRequest
            {
                Document = document,
                Languages = languages,
                Glossary = glossary,
                AllowCloudProvider = request.AllowCloudProvider,
            },
            provider,
            cancellationToken);

        logger.DocumentationTranslated(
            translation.SegmentCount, translation.CachedCount, translation.TranslatedCount, translation.UnchangedCount);

        return new TranslateDocumentationResult
        {
            DocumentationXml = DocumentationXmlWriter.Write(translation.Document),
            Outcome = OutcomeOf(translation),
            SegmentCount = translation.SegmentCount,
            CachedCount = translation.CachedCount,
            TranslatedCount = translation.TranslatedCount,
            UnchangedCount = translation.UnchangedCount,
            ProviderFailure = translation.ProviderFailure,
        };
    }

    public Task<ModelInstallStatus> GetModelStatusAsync(
        string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        LanguagePair? pair = LanguagePair.Create(sourceLanguage, targetLanguage);

        return pair is null ? Task.FromResult(InvalidLanguageStatus()) : modelInstaller.GetStatusAsync(pair, cancellationToken);
    }

    public Task<ModelInstallStatus> StartModelInstallAsync(
        string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        LanguagePair? pair = LanguagePair.Create(sourceLanguage, targetLanguage);

        return pair is null ? Task.FromResult(InvalidLanguageStatus()) : modelInstaller.StartInstallAsync(pair, cancellationToken);
    }

    // A direction that is not even a valid pair of languages has no model to install.
    private static ModelInstallStatus InvalidLanguageStatus() => new()
    {
        State = ModelInstallState.Unavailable,
        Detail = "The language is not valid.",
    };

    private static TranslationOutcome OutcomeOf(TranslationResult translation)
    {
        if (translation.BlockedByCloudConsent)
        {
            return TranslationOutcome.CloudConsentRequired;
        }

        return translation.ProviderFailure is not null ? TranslationOutcome.ProviderFailed : TranslationOutcome.Completed;
    }

    // The document goes back through the writer even when nothing was translated, so a client
    // always receives XML in the one shape it can rely on, not whatever it happened to send.
    private static TranslateDocumentationResult Unchanged(DocumentModel document, int segmentCount, TranslationOutcome outcome) =>
        new()
        {
            DocumentationXml = DocumentationXmlWriter.Write(document),
            Outcome = outcome,
            SegmentCount = segmentCount,
            CachedCount = 0,
            TranslatedCount = 0,
            UnchangedCount = segmentCount,
        };

    private static string GetServiceVersion() =>
        typeof(TranslatorService).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}