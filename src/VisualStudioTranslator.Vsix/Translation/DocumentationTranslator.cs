using System.Globalization;
using Microsoft.VisualStudio.Shell;
using StreamJsonRpc;
using VisualStudioTranslator.Core.Concurrency;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Vsix.Service;
using VisualStudioTranslator.Vsix.Setup;

namespace VisualStudioTranslator.Vsix.Translation;

/// <summary>
/// Asks the Engine to translate a documentation comment. The caller decides how long to wait: a tooltip
/// waits a moment and then shows the translation when it arrives. Hovers over the same comment while it
/// is being translated share one request, and a finished one fills the Engine's cache, so the next hover
/// over the same symbol is answered at once.
/// </summary>
internal sealed class DocumentationTranslator(EngineConnection connection)
{
    private const string Source = nameof(DocumentationTranslator);

    // Documentation is written in English until the user can say otherwise.
    private const string SourceLanguage = "en";

    // A local model takes a few seconds a segment and a long comment has many, after a few seconds of loading
    // the first time. A request that has not finished by then has something wrong with it.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    private readonly InFlightRequests<DocumentModel?> _inFlight = new();
    private readonly object _reportedSync = new();
    private readonly HashSet<TranslationOutcome> _reportedOutcomes = [];
    private bool _loggedNoTarget;

    static DocumentationTranslator()
    {
        // The first line of the log says which build is running, which settles at once whether a
        // problem is in the code being looked at or in an older copy still loaded.
        string location = typeof(DocumentationTranslator).Assembly.Location;
        DiagnosticLog.Info(Source, $"Extension loaded from {location} (built {File.GetLastWriteTimeUtc(location):u}).");
    }

    public static DocumentationTranslator Shared { get; } = new(EngineConnection.Shared);

    /// <summary>
    /// Starts translating a documentation comment, or joins a translation of the same comment already under
    /// way, without waiting for it.
    /// </summary>
    /// <returns>
    /// The work, which gives the translated document, or <see langword="null"/> when there is nothing to show:
    /// no language to translate into (then at once), or the Engine could not translate it.
    /// </returns>
    public Task<DocumentModel?> TranslateAsync(string documentationXml)
    {
        string? targetLanguage = GetTargetLanguage();
        if (targetLanguage is null)
        {
            if (!_loggedNoTarget)
            {
                _loggedNoTarget = true;
                DiagnosticLog.Info(Source, $"Nothing to translate into: the display language is '{CultureInfo.CurrentUICulture.Name}'.");
            }

            return Task.FromResult<DocumentModel?>(null);
        }

        return _inFlight.GetOrStart(
            targetLanguage + "\n" + documentationXml,
            () => RequestAsync(documentationXml, targetLanguage));
    }

    // For now the language of Windows' display decides. A setting replaces this once there is a
    // place for settings. An English display needs no translation of English documentation.
    private static string? GetTargetLanguage()
    {
        string tag = CultureInfo.CurrentUICulture.Name;

        if (tag.Length == 0 || (tag.StartsWith("en", StringComparison.OrdinalIgnoreCase) && (tag.Length == 2 || tag[2] == '-')))
        {
            return null;
        }

        return tag;
    }

    private async Task<DocumentModel?> RequestAsync(string documentationXml, string targetLanguage)
    {
        ITranslatorService? service = null;
        using CancellationTokenSource timeout = new(RequestTimeout);

        try
        {
            service = await connection.GetServiceAsync(timeout.Token).ConfigureAwait(false);

            TranslateDocumentationResult result = await service.TranslateDocumentationAsync(
                new TranslateDocumentationRequest
                {
                    DocumentationXml = documentationXml,
                    SourceLanguage = SourceLanguage,
                    TargetLanguage = targetLanguage,
                },
                timeout.Token).ConfigureAwait(false);

            DiagnosticLog.Info(
                Source,
                $"The Engine answered {result.Outcome}: {result.TranslatedCount} translated, {result.CachedCount} cached, {result.UnchangedCount} unchanged.");

            if (result.TranslatedCount + result.CachedCount == 0)
            {
                ReportOnce(result, targetLanguage);
                return null;
            }

            return DocumentationXmlParser.Parse(result.DocumentationXml);
        }
        catch (Exception ex)
        {
            // A call the Engine answered with an error leaves the connection healthy; anything
            // else (a broken pipe, a timeout) may not, so the next request connects afresh.
            if (service != null && ex is not RemoteInvocationException)
            {
                connection.ReportFailure(service);
            }

            DiagnosticLog.Error(Source, $"Translation request failed: {ex}");
            return null;
        }
    }

    // Logged once per kind, not per hover: "no model installed" would otherwise fill the log.
    private void ReportOnce(TranslateDocumentationResult result, string targetLanguage)
    {
        if (result.Outcome == TranslationOutcome.NoProviderAvailable)
        {
            // Quite possibly just because no model is installed yet. The setup prompt works that out
            // (without touching the network) and, if so, offers to download one; it is a no-op for any
            // language it has already dealt with this session.
            ModelSetupPrompt.Shared.Offer(targetLanguage);
        }

        if (result.Outcome == TranslationOutcome.Completed)
        {
            return;
        }

        lock (_reportedSync)
        {
            if (!_reportedOutcomes.Add(result.Outcome))
            {
                return;
            }
        }

        string detail = result.ProviderFailure.HasValue ? $" ({result.ProviderFailure.Value})" : string.Empty;
        ActivityLog.LogWarning(Source, $"No translation was produced: {result.Outcome}{detail}.");
    }
}