using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Utilities.UnifiedSettings;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Vsix.Service;
using UnifiedSettingsManager = Microsoft.VisualStudio.Utilities.UnifiedSettings.ISettingsManager;

namespace VisualStudioTranslator.Vsix.Options;

/// <summary>What the user has set, as the extension uses it.</summary>
/// <param name="TranslateTooltips">Whether tooltips get a translation at all.</param>
/// <param name="TargetLanguage">The language chosen, or <see cref="TargetLanguages.FollowVisualStudio"/>.</param>
/// <param name="DisplayLanguage">The language Visual Studio is shown in, as its UI thread knows it.</param>
/// <param name="Model">The model chosen, as its tag (see <see cref="LocalModelSettings.ModelTags"/>).</param>
/// <param name="UseGraphicsCard">Whether the model may run on a graphics card.</param>
internal sealed record TranslatorSettings(
    bool TranslateTooltips, string TargetLanguage, string DisplayLanguage, string Model, bool UseGraphicsCard)
{
    /// <summary>The language to translate into, or <see langword="null"/> when there is none.</summary>
    public string? ResolvedTargetLanguage =>
        TranslateTooltips ? TargetLanguages.Resolve(TargetLanguage, DisplayLanguage) : null;

    /// <summary>The settings the Engine applies to translation on this computer.</summary>
    public LocalModelSettings LocalModel => new()
    {
        Model = LocalModelSettings.ParseModel(Model),
        UseGraphicsCard = UseGraphicsCard,
    };
}

/// <summary>
/// Reads the extension's settings from wherever this Visual Studio keeps them: the new settings experience
/// (Unified Settings, declared in <c>Options\registration.json</c>) when it is on, which it is in Visual Studio
/// 2026, or else the classic <see cref="GeneralOptionsPage"/>.
/// <para>
/// Both are read on the UI thread, which is where Visual Studio's settings live. A hover asks often, so the
/// last answer is kept for a moment: a changed setting takes effect within two seconds, without a restart.
/// </para>
/// </summary>
internal sealed class TranslatorOptions
{
    public const string TranslateTooltipsMoniker = "visualStudioTranslator.general.translateTooltips";
    public const string TargetLanguageMoniker = "visualStudioTranslator.general.targetLanguage";
    public const string ModelMoniker = "visualStudioTranslator.localModel.model";
    public const string UseGraphicsCardMoniker = "visualStudioTranslator.localModel.useGraphicsCard";

    private const string Source = nameof(TranslatorOptions);

    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(2);

    private VisualStudioTranslatorPackage? _package;
    private UnifiedSettingsManager? _unified;
    private Snapshot? _last;
    private bool _loggedUnifiedFailure;

    public static TranslatorOptions Shared { get; } = new();

    /// <summary>Called by the package as it loads; until then the defaults apply.</summary>
    public async Task AttachAsync(VisualStudioTranslatorPackage package)
    {
        _unified = await package.GetServiceAsync(typeof(UnifiedSettingsService)).ConfigureAwait(false)
            as UnifiedSettingsManager;
        _package = package;
    }

    public async Task<TranslatorSettings> ReadAsync(CancellationToken cancellationToken)
    {
        Snapshot? last = Volatile.Read(ref _last);

        if (last is not null && Stopwatch.GetTimestamp() - last.ReadAt < Freshness.TotalSeconds * Stopwatch.Frequency)
        {
            return last.Settings;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        TranslatorSettings settings = ReadOnUIThread();
        Volatile.Write(ref _last, new Snapshot(settings, Stopwatch.GetTimestamp()));

        return settings;
    }

    private TranslatorSettings ReadOnUIThread()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string displayLanguage = CultureInfo.CurrentUICulture.Name;

        if (TryReadUnified(displayLanguage) is { } unified)
        {
            return unified;
        }

        if (_package?.GeneralOptions is { } page)
        {
            return new TranslatorSettings(
                page.TranslateTooltips, page.TargetLanguage, displayLanguage, page.Model, page.UseGraphicsCard);
        }

        return new TranslatorSettings(
            true, TargetLanguages.FollowVisualStudio, displayLanguage, ModelChoiceConverter.Automatic, true);
    }

    // Null when Unified Settings is off or did not answer; the classic page holds the settings then.
    private TranslatorSettings? TryReadUnified(string displayLanguage)
    {
        if (_unified is null)
        {
            return null;
        }

        try
        {
            ISettingsReader reader = _unified.GetReader();
            SettingRetrieval<bool> translate = reader.GetValue<bool>(TranslateTooltipsMoniker);
            SettingRetrieval<string> language = reader.GetValue<string>(TargetLanguageMoniker);

            if (translate.Outcome == SettingRetrievalOutcome.Success && language.Outcome == SettingRetrievalOutcome.Success)
            {
                // The settings added later fall back to their defaults on their own, so a Visual Studio that has not
                // picked them up yet still reads the ones it has from here rather than from the classic page.
                SettingRetrieval<string> model = reader.GetValue<string>(ModelMoniker);
                SettingRetrieval<bool> useGraphicsCard = reader.GetValue<bool>(UseGraphicsCardMoniker);

                return new TranslatorSettings(
                    translate.Value,
                    language.Value ?? TargetLanguages.FollowVisualStudio,
                    displayLanguage,
                    model.Outcome == SettingRetrievalOutcome.Success
                        ? model.Value ?? ModelChoiceConverter.Automatic
                        : ModelChoiceConverter.Automatic,
                    useGraphicsCard.Outcome != SettingRetrievalOutcome.Success || useGraphicsCard.Value);
            }

            return null;
        }
        catch (Exception ex)
        {
            if (!_loggedUnifiedFailure)
            {
                _loggedUnifiedFailure = true;
                DiagnosticLog.Info(
                    Source, $"Reading Unified Settings failed; using the classic settings page: {ex.GetType().Name}.");
            }

            return null;
        }
    }

    private sealed record Snapshot(TranslatorSettings Settings, long ReadAt);

    // The service behind Unified Settings. Its own type is in an assembly Visual Studio keeps internal, but a
    // service is found by the GUID of the type asked for, so a type carrying the same GUID finds it.
    [Guid("e3684f31-344e-42ea-9047-b620fdc7ac25")]
    private static class UnifiedSettingsService
    {
    }
}