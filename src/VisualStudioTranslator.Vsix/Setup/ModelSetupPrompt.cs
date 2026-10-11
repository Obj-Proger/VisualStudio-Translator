using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Vsix.Service;

namespace VisualStudioTranslator.Vsix.Setup;

/// <summary>
/// Offers to download the language model that translating needs, and does it if the user agrees.
/// Nothing is downloaded, and nothing is even looked up on the network, until they press the button:
/// what is shown first is only the Engine's own knowledge that no model is installed.
/// <para>
/// A direction is dealt with at most once per session: once the question has been asked, or turned out
/// not to apply, or been declined for good. An attempt that did not get that far (the Engine was not
/// reachable yet, Visual Studio had nowhere to put the bar) is tried again after a short wait, so a hiccup does
/// not cost the user the offer for the rest of the session.
/// </para>
/// </summary>
internal sealed class ModelSetupPrompt(EngineConnection connection)
{
    private const string Source = nameof(ModelSetupPrompt);
    private const string SourceLanguage = "en";
    private const string DownloadLabel = "Download";
    private const string NotNowLabel = "Not now";
    private const string NeverLabel = "Don't ask again";
    private const string TryAgainLabel = "Try again";

    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly object _sync = new();

    // Per language: null once it has been dealt with this session, or the time of an attempt that has not
    // got that far, which blocks another attempt until RetryAfter has passed.
    private readonly Dictionary<string, DateTime?> _state = [];

    public static ModelSetupPrompt Shared { get; } = new(EngineConnection.Shared);

    /// <summary>Called when translating into <paramref name="targetLanguage"/> found no provider that could. Returns at once.</summary>
    public void Offer(string targetLanguage)
    {
        string key = targetLanguage.ToLowerInvariant();

        lock (_sync)
        {
            if (_state.TryGetValue(key, out DateTime? attempted)
                && (attempted is null || DateTime.UtcNow - attempted.Value < RetryAfter))
            {
                return;
            }

            _state[key] = DateTime.UtcNow;
        }

        DiagnosticLog.Info(Source, $"No provider could translate into {targetLanguage}; checking whether a language model is missing.");
        _ = Task.Run(() => OfferAsync(targetLanguage, key));
    }

    private void MarkHandled(string key)
    {
        lock (_sync)
        {
            _state[key] = null;
        }
    }

    private async Task OfferAsync(string targetLanguage, string key)
    {
        try
        {
            if (DeclinedModelPrompts.IsDeclined(targetLanguage))
            {
                DiagnosticLog.Info(Source, $"Not offering a model for {targetLanguage}: the user said not to ask again.");
                MarkHandled(key);
                return;
            }

            ITranslatorService service = await connection.GetServiceAsync(CancellationToken.None).ConfigureAwait(false);
            ModelInstallStatus status = await service
                .GetModelStatusAsync(SourceLanguage, targetLanguage, CancellationToken.None).ConfigureAwait(false);

            DiagnosticLog.Info(Source, $"The Engine says the model for {targetLanguage} is: {status.State}.");

            // Only "no model yet" is worth asking about. A language the server has no model for, or a
            // download already running, is not a question for the user.
            if (status.State != ModelInstallState.NotInstalled)
            {
                MarkHandled(key);
                return;
            }

            string message =
                $"Visual Studio Translator can translate documentation into {DisplayName(targetLanguage)} entirely on this computer. " +
                $"That needs a one-time download of a language model (up to about 50 MB) from {status.SourceHost}. " +
                "Nothing from your code is sent.";

            InfoBarHandle? bar = null;
            bar = await InfoBars.ShowAsync(
                message,
                [DownloadLabel, NotNowLabel, NeverLabel],
                async label =>
                {
                    if (bar != null)
                    {
                        await bar.CloseAsync().ConfigureAwait(false);
                    }

                    if (label == DownloadLabel)
                    {
                        await InstallAsync(targetLanguage).ConfigureAwait(false);
                    }
                    else if (label == NeverLabel)
                    {
                        DeclinedModelPrompts.Decline(targetLanguage);
                    }
                }).ConfigureAwait(false);

            // No bar means Visual Studio had nowhere to show one. That is worth another go later, not worth giving up on.
            if (bar != null)
            {
                MarkHandled(key);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error(Source, $"Offering a language model failed: {ex}");
        }
    }

    // The user has agreed. The Engine does the work; this just starts it and keeps the user informed.
    private async Task InstallAsync(string targetLanguage)
    {
        try
        {
            ITranslatorService service = await connection.GetServiceAsync(CancellationToken.None).ConfigureAwait(false);
            ModelInstallStatus status = await service
                .StartModelInstallAsync(SourceLanguage, targetLanguage, CancellationToken.None).ConfigureAwait(false);

            DiagnosticLog.Info(Source, $"Installing the model for {targetLanguage} was started: {status.State}.");

            DateTime deadline = DateTime.UtcNow.AddMinutes(35);

            while (status.State == ModelInstallState.Installing && DateTime.UtcNow < deadline)
            {
                await ShowStatusAsync(DescribeProgress(status)).ConfigureAwait(false);
                await Task.Delay(750).ConfigureAwait(false);

                status = await service
                    .GetModelStatusAsync(SourceLanguage, targetLanguage, CancellationToken.None).ConfigureAwait(false);
            }

            DiagnosticLog.Info(Source, $"Installing the model for {targetLanguage} ended: {status.State}.");

            if (status.State == ModelInstallState.Installed)
            {
                await ShowStatusAsync("Language model installed. Documentation is translated as you hover.").ConfigureAwait(false);
                return;
            }

            await ShowStatusAsync(string.Empty).ConfigureAwait(false);
            await OfferRetryAsync(targetLanguage, status.Detail ?? "The language model could not be installed.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error(Source, $"Installing a language model failed: {ex}");
            await OfferRetryAsync(targetLanguage, "The language model could not be installed.").ConfigureAwait(false);
        }
    }

    private async Task OfferRetryAsync(string targetLanguage, string reason)
    {
        InfoBarHandle? bar = null;
        bar = await InfoBars.ShowAsync(
            reason,
            [TryAgainLabel],
            async label =>
            {
                if (bar != null)
                {
                    await bar.CloseAsync().ConfigureAwait(false);
                }

                await InstallAsync(targetLanguage).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    private static string DescribeProgress(ModelInstallStatus status)
    {
        if (status.TotalBytes <= 0)
        {
            return "Downloading the language model...";
        }

        return $"Downloading the language model: {ToMegabytes(status.DoneBytes)} of {ToMegabytes(status.TotalBytes)} MB";
    }

    private static string ToMegabytes(long bytes) =>
        (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture);

    private static string DisplayName(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return language;
        }
    }

    // The status bar is the quiet place for progress; a bar over the editor for every percent would be a nuisance.
    // The service is fetched before the main thread is entered, for the reason given in InfoBars.ShowAsync.
    private static async Task ShowStatusAsync(string text)
    {
        object? service = await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(SVsStatusbar));

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (service is IVsStatusbar statusBar)
        {
            statusBar.SetText(text);
        }
    }
}