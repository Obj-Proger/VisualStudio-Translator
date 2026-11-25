using StreamJsonRpc;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Vsix.Options;
using VisualStudioTranslator.Vsix.Resources;
using VisualStudioTranslator.Vsix.Service;
using VisualStudioTranslator.Vsix.Setup;

namespace VisualStudioTranslator.Vsix.Translation;

/// <summary>What became of a text sent to be translated.</summary>
/// <param name="Text">The translation, or <see langword="null"/> when there is none to show.</param>
/// <param name="Problem">Why there is none, or why part of it is still in the original language; for the user.</param>
internal sealed record TextTranslation(string? Text, string? Problem);

/// <summary>
/// Asks the Engine to translate plain text, for the translator window. Unlike documentation, which is translated as
/// the user hovers and may simply not be shown, the window waits for this answer, so every way it can end is told
/// to the user in a sentence.
/// </summary>
internal sealed class TextTranslator(EngineConnection connection)
{
    private const string Source = nameof(TextTranslator);

    // A local model takes a few seconds a paragraph; a long text has many.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(15);

    public static TextTranslator Shared { get; } = new(EngineConnection.Shared);

    public async Task<TextTranslation> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TextTranslation(null, null);
        }

        if (text.Length > TranslateTextRequest.MaxLength)
        {
            return new TextTranslation(null, TooLong);
        }

        TranslatorSettings settings = await TranslatorOptions.Shared.ReadAsync(cancellationToken).ConfigureAwait(false);
        ITranslatorService? service = null;
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            service = await connection.GetServiceAsync(timeout.Token).ConfigureAwait(false);
            await connection.SendLocalModelSettingsAsync(service, settings.LocalModel, timeout.Token).ConfigureAwait(false);

            TranslateTextResult result = await service.TranslateTextAsync(
                new TranslateTextRequest
                {
                    Text = text,
                    SourceLanguage = LanguageTag.Undetermined,
                    TargetLanguage = targetLanguage,
                },
                timeout.Token).ConfigureAwait(false);

            DiagnosticLog.Info(
                Source,
                $"The Engine answered {result.Outcome}: {result.TranslatedCount} translated, " +
                $"{result.CachedCount} cached, {result.UnchangedCount} unchanged.");

            if (result.Outcome == TranslationOutcome.NoProviderAvailable)
            {
                ModelSetupPrompt.Shared.Offer(targetLanguage, settings.LocalModel);
            }

            return Describe(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RemoteMethodNotFoundException)
        {
            // An Engine from before the window existed, still running after the extension was updated.
            return new TextTranslation(null, UiText.ServiceUpdating);
        }
        catch (Exception ex)
        {
            if (service != null && ex is not RemoteInvocationException)
            {
                connection.ReportFailure(service);
            }

            DiagnosticLog.Error(Source, $"Translating text failed: {ex}");

            return new TextTranslation(
                null,
                ex is OperationCanceledException
                    ? UiText.TookTooLong
                    : UiText.ServiceUnavailable);
        }
    }

    private static string TooLong =>
        UiText.Format(UiText.TextTooLong, TranslateTextRequest.MaxLength.ToString("N0", UiText.Culture));

    private static TextTranslation Describe(TranslateTextResult result)
    {
        bool translatedAny = result.TranslatedCount + result.CachedCount > 0;

        return result.Outcome switch
        {
            TranslationOutcome.NoProviderAvailable => new(null, UiText.NoModel),
            TranslationOutcome.TextTooLong => new(null, TooLong),
            TranslationOutcome.InvalidLanguage => new(null, UiText.InvalidLanguage),
            TranslationOutcome.CloudConsentRequired => new(null, UiText.CloudConsent),
            TranslationOutcome.ProviderFailed when !translatedAny => new(null, UiText.TranslationFailed),
            _ when !translatedAny => new(null, UiText.NothingTranslated),
            _ when result.UnchangedCount > 0 => new(result.Text, UiText.PartlyTranslated),
            _ => new(result.Text, null),
        };
    }
}