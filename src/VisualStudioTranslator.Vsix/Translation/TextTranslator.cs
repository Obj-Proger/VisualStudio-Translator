using StreamJsonRpc;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Vsix.Options;
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
            return new TextTranslation(null, "The translation service is being updated. Try again in a few minutes.");
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
                    ? "The translation took too long and was stopped."
                    : "The translation service is not available. Try again.");
        }
    }

    private static string TooLong =>
        $"The text is too long. Up to {TranslateTextRequest.MaxLength:N0} characters can be translated at a time.";

    private static TextTranslation Describe(TranslateTextResult result)
    {
        bool translatedAny = result.TranslatedCount + result.CachedCount > 0;

        return result.Outcome switch
        {
            TranslationOutcome.NoProviderAvailable => new(
                null, "No language model is installed yet. Download it from the bar at the top of Visual Studio."),
            TranslationOutcome.TextTooLong => new(null, TooLong),
            TranslationOutcome.InvalidLanguage => new(null, "The language chosen is not one that can be translated into."),
            TranslationOutcome.CloudConsentRequired => new(null, "Only an online service can translate this, and it is not allowed."),
            TranslationOutcome.ProviderFailed when !translatedAny => new(null, "The translation failed. Try again."),
            _ when !translatedAny => new(null, "Nothing was translated. The text may already be in this language."),
            _ when result.UnchangedCount > 0 => new(result.Text, "Some parts could not be translated and are left as they were."),
            _ => new(result.Text, null),
        };
    }
}