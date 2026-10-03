using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using StreamJsonRpc;
using VisualStudioTranslator.Core.Concurrency;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Vsix.Service;

namespace VisualStudioTranslator.Vsix.Translation;

/// <summary>
/// Asks the Engine to translate a documentation comment, waiting only as long as the caller can
/// afford. A tooltip cannot appear until every source has answered, so a slow first translation
/// (the Engine starting, a model loading) must not hold it up. When the time runs out the caller
/// gets nothing, but the request is not abandoned: it carries on and fills the Engine's cache,
/// so the next hover over the same symbol is answered at once.
/// </summary>
internal sealed class DocumentationTranslator
{
    // Documentation is written in English until the user can say otherwise.
    private const string SourceLanguage = "en";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private readonly EngineConnection _connection;
    private readonly InFlightRequests<DocumentModel?> _inFlight = new InFlightRequests<DocumentModel?>();
    private readonly object _reportedSync = new object();
    private readonly HashSet<TranslationOutcome> _reportedOutcomes = new HashSet<TranslationOutcome>();

    public DocumentationTranslator(EngineConnection connection)
    {
        _connection = connection;
    }

    public static DocumentationTranslator Shared { get; } = new DocumentationTranslator(EngineConnection.Shared);

    /// <returns>
    /// The translated document, or <see langword="null"/> when there is nothing to show: no target
    /// language, the Engine could not translate it, or it did not finish within <paramref name="budget"/>.
    /// </returns>
    public async Task<DocumentModel?> TryTranslateAsync(string documentationXml, TimeSpan budget, CancellationToken cancellationToken)
    {
        string? targetLanguage = GetTargetLanguage();
        if (targetLanguage is null)
        {
            return null;
        }

        Task<DocumentModel?> work = _inFlight.GetOrStart(
            targetLanguage + "\n" + documentationXml,
            () => TranslateAsync(documentationXml, targetLanguage));

        if (!await work.CompletesWithinAsync(budget, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await work.ConfigureAwait(false);
    }

    // For now the language of Windows' display decides. A setting replaces this once there is a
    // place for settings. An English display needs no translation of English documentation.
    private static string? GetTargetLanguage()
    {
        string tag = CultureInfo.CurrentUICulture.Name;

        if (tag.Length == 0 || tag.StartsWith("en", StringComparison.OrdinalIgnoreCase) && (tag.Length == 2 || tag[2] == '-'))
        {
            return null;
        }

        return tag;
    }

    private async Task<DocumentModel?> TranslateAsync(string documentationXml, string targetLanguage)
    {
        ITranslatorService? service = null;

        using (CancellationTokenSource timeout = new CancellationTokenSource(RequestTimeout))
        {
            try
            {
                service = await _connection.GetServiceAsync(timeout.Token).ConfigureAwait(false);

                TranslateDocumentationResult result = await service.TranslateDocumentationAsync(
                    new TranslateDocumentationRequest
                    {
                        DocumentationXml = documentationXml,
                        SourceLanguage = SourceLanguage,
                        TargetLanguage = targetLanguage,
                    },
                    timeout.Token).ConfigureAwait(false);

                if (result.TranslatedCount + result.CachedCount == 0)
                {
                    ReportOnce(result);
                    return null;
                }

                return DocumentationXmlParser.Parse(result.DocumentationXml);
            }
            catch (Exception ex)
            {
                // A call the Engine answered with an error leaves the connection healthy; anything
                // else (a broken pipe, a timeout) may not, so the next request connects afresh.
                if (service != null && !(ex is RemoteInvocationException))
                {
                    _connection.ReportFailure(service);
                }

                ActivityLog.LogError(nameof(DocumentationTranslator), $"Translation request failed: {ex}");
                return null;
            }
        }
    }

    // Logged once per kind, not per hover: "no model installed" would otherwise fill the log.
    private void ReportOnce(TranslateDocumentationResult result)
    {
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
        ActivityLog.LogWarning(nameof(DocumentationTranslator), $"No translation was produced: {result.Outcome}{detail}.");
    }
}