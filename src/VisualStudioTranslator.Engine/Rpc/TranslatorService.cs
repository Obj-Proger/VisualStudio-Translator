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
    IModelInstaller modelInstaller,
    ILocalModelConfiguration localModel) : ITranslatorService
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

        Translated translation = await TranslateAsync(
            document,
            request.SourceLanguage,
            request.TargetLanguage,
            request.Glossary,
            request.AllowCloudProvider,
            cancellationToken).ConfigureAwait(false);

        return new TranslateDocumentationResult
        {
            DocumentationXml = DocumentationXmlWriter.Write(translation.Document),
            Outcome = translation.Outcome,
            SegmentCount = translation.SegmentCount,
            CachedCount = translation.CachedCount,
            TranslatedCount = translation.TranslatedCount,
            UnchangedCount = translation.UnchangedCount,
            ProviderFailure = translation.ProviderFailure,
        };
    }

    public async Task<TranslateTextResult> TranslateTextAsync(TranslateTextRequest request, CancellationToken cancellationToken)
    {
        string text = request.Text ?? string.Empty;
        PlainTextDocument layout = PlainText.Parse(text);
        Translated translation;

        if (text.Length > TranslateTextRequest.MaxLength)
        {
            translation = Unchanged(layout.Document, TranslationOutcome.TextTooLong);
        }
        else
        {
            translation = await TranslateAsync(
                layout.Document,
                request.SourceLanguage,
                request.TargetLanguage,
                request.Glossary,
                request.AllowCloudProvider,
                cancellationToken).ConfigureAwait(false);
        }

        return new TranslateTextResult
        {
            // Text left wholly as it was goes back exactly as it came, line breaks and all, not as laid out again.
            Text = translation.TranslatedCount + translation.CachedCount == 0 ? text : PlainText.Write(layout, translation.Document),
            Outcome = translation.Outcome,
            SegmentCount = translation.SegmentCount,
            CachedCount = translation.CachedCount,
            TranslatedCount = translation.TranslatedCount,
            UnchangedCount = translation.UnchangedCount,
            ProviderFailure = translation.ProviderFailure,
        };
    }

    public async Task<ModelInstallStatus> GetModelStatusAsync(
    string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        LanguagePair? pair = LanguagePair.Create(sourceLanguage, targetLanguage);

        if (pair is null)
        {
            return InvalidLanguageStatus();
        }

        ModelInstallStatus status = await modelInstaller.GetStatusAsync(pair, cancellationToken);
        logger.ModelStatusAnswered(pair, status.State);
        return status;
    }

    public async Task<ModelInstallStatus> StartModelInstallAsync(
        string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        LanguagePair? pair = LanguagePair.Create(sourceLanguage, targetLanguage);

        if (pair is null)
        {
            return InvalidLanguageStatus();
        }

        ModelInstallStatus status = await modelInstaller.StartInstallAsync(pair, cancellationToken);
        logger.ModelInstallRequested(pair, status.State);
        return status;
    }

    public async Task ApplyLocalModelSettingsAsync(LocalModelSettings settings, CancellationToken cancellationToken)
    {
        // The wire is the one place a missing value can arrive from; it means the defaults.
        await localModel.ApplyAsync(settings ?? new LocalModelSettings(), cancellationToken).ConfigureAwait(false);
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

    // What documentation and plain text share: the languages checked, a provider chosen, the document translated.
    private async Task<Translated> TranslateAsync(
        DocumentModel document,
        string sourceLanguage,
        string targetLanguage,
        IReadOnlyList<GlossaryEntry>? glossaryEntries,
        bool allowCloudProvider,
        CancellationToken cancellationToken)
    {
        LanguagePair? languages = LanguagePair.Create(sourceLanguage, targetLanguage);
        if (languages is null)
        {
            return Unchanged(document, TranslationOutcome.InvalidLanguage);
        }

        ProviderSelection selection = ProviderSelector.Select(providers, languages, allowCloudProvider);
        if (selection.Provider is not { } provider)
        {
            if (selection.CloudConsentRequired)
            {
                return Unchanged(document, TranslationOutcome.CloudConsentRequired);
            }

            logger.NoProviderAvailable(languages);
            return Unchanged(document, TranslationOutcome.NoProviderAvailable);
        }

        // The wire is the one place a malformed entry can arrive. A blank term would match
        // everywhere once it became part of a pattern, so it never gets that far.
        Glossary glossary = new()
        {
            Entries = [.. (glossaryEntries ?? []).Where(entry => !string.IsNullOrWhiteSpace(entry.Term))],
        };

        TranslationResult translation = await orchestrator.TranslateAsync(
            new TranslationRequest
            {
                Document = document,
                Languages = languages,
                Glossary = glossary,
                AllowCloudProvider = allowCloudProvider,
            },
            provider,
            cancellationToken).ConfigureAwait(false);

        logger.DocumentationTranslated(
            translation.SegmentCount, translation.CachedCount, translation.TranslatedCount, translation.UnchangedCount);

        return new Translated(
            translation.Document,
            OutcomeOf(translation),
            translation.SegmentCount,
            translation.CachedCount,
            translation.TranslatedCount,
            translation.UnchangedCount,
            translation.ProviderFailure);
    }

    private static Translated Unchanged(DocumentModel document, TranslationOutcome outcome)
    {
        int segmentCount = DocumentSegmenter.Segment(document).Count;

        return new Translated(document, outcome, segmentCount, 0, 0, segmentCount, ProviderFailure: null);
    }

    private static string GetServiceVersion() =>
        typeof(TranslatorService).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    // A translation as both kinds of request answer it.
    private sealed record Translated(
        DocumentModel Document,
        TranslationOutcome Outcome,
        int SegmentCount,
        int CachedCount,
        int TranslatedCount,
        int UnchangedCount,
        ProviderFailureKind? ProviderFailure);
}