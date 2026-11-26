using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace VisualStudioTranslator.Vsix.Resources;

/// <summary>
/// The extension's own words, in the language Visual Studio is shown in: captions in tooltips, the translator window,
/// the bar offering the model. They come from Strings.resx and its translations, one file per language the extension
/// translates into; a language without its own file falls back to English. Where Visual Studio has its own word for
/// the same thing, the translation uses that word.
/// </summary>
internal static class UiText
{
    private static readonly ResourceManager Strings = new("VisualStudioTranslator.Vsix.Resources.Strings", typeof(UiText).Assembly);

    private static CultureInfo? _culture;

    /// <summary>
    /// The language Visual Studio is shown in. Set from its UI thread, whose culture is that language; other threads
    /// may have another, so they are not asked.
    /// </summary>
    public static CultureInfo Culture
    {
        get => Volatile.Read(ref _culture) ?? CultureInfo.CurrentUICulture;
        set => Volatile.Write(ref _culture, value);
    }

    public static string TranslationLabel => Get();

    public static string Translating => Get();

    public static string WindowCaption => Get();

    public static string TranslateIntoLabel => Get();

    public static string TranslateIntoName => Get();

    public static string SourceTextName => Get();

    public static string TranslateButton => Get();

    public static string TranslateButtonTip => Get();

    public static string CopyButton => Get();

    public static string CopyButtonTip => Get();

    public static string ServiceUpdating => Get();

    public static string TookTooLong => Get();

    public static string ServiceUnavailable => Get();

    public static string TextTooLong => Get();

    public static string NoModel => Get();

    public static string InvalidLanguage => Get();

    public static string CloudConsent => Get();

    public static string TranslationFailed => Get();

    public static string NothingTranslated => Get();

    public static string PartlyTranslated => Get();

    public static string ModelOffer => Get();

    public static string DownloadButton => Get();

    public static string NotNowButton => Get();

    public static string NeverButton => Get();

    public static string TryAgainButton => Get();

    public static string ModelInstalled => Get();

    public static string ModelInstallFailed => Get();

    public static string Downloading => Get();

    public static string DownloadingProgress => Get();

    public static string SizeUnknown => Get();

    public static string SizeMegabytes => Get();

    public static string SizeGigabytes => Get();

    public static string SettingsDescription => Get();

    public static string SettingsGeneral => Get();

    public static string SettingsLocalTranslation => Get();

    public static string SettingsLocalTranslationDescription => Get();

    public static string SettingsTranslateTooltips => Get();

    public static string SettingsTranslateTooltipsDescription => Get();

    public static string SettingsTargetLanguage => Get();

    public static string SettingsTargetLanguageDescription => Get();

    public static string SettingsTranslateErrors => Get();

    public static string SettingsTranslateErrorsDescription => Get();

    public static string SettingsReplaceOriginal => Get();

    public static string SettingsReplaceOriginalDescription => Get();

    public static string ShowOriginal => Get();

    public static string ShowTranslation => Get();

    public static string SettingsSameAsVisualStudio => Get();

    public static string SettingsModel => Get();

    public static string SettingsModelDescription => Get();

    public static string SettingsModelAutomatic => Get();

    public static string SettingsModelQuality => Get();

    public static string SettingsModelLight => Get();

    public static string SettingsUseGraphicsCard => Get();

    public static string SettingsUseGraphicsCardDescription => Get();

    /// <summary>
    /// A text by its name, for the settings page, whose attributes can only name the text they show; the name of the
    /// property above that would return it.
    /// </summary>
    public static string ByName(string name) => Get(name);

    /// <summary>A text with placeholders filled in, numbers formatted for the same language.</summary>
    public static string Format(string text, params object[] arguments) => string.Format(Culture, text, arguments);

    private static string Get([CallerMemberName] string name = "") => Strings.GetString(name, Culture) ?? name;
}