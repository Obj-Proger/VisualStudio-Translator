using System.ComponentModel;
using Microsoft.VisualStudio.Shell;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Vsix.Resources;

namespace VisualStudioTranslator.Vsix.Options;

/// <summary>
/// The settings page for a Visual Studio that has the new settings experience (Unified Settings) turned off,
/// as Visual Studio 2022 has by default. Where it is on, Visual Studio hides this page and shows the same
/// settings from <c>Options\registration.json</c> instead; <see cref="TranslatorOptions"/> reads whichever is in use.
/// </summary>
public sealed class GeneralOptionsPage : DialogPage
{
    // The names of UiText's texts, which the attributes below show in the language of Visual Studio.
    private const string Category = nameof(UiText.SettingsGeneral);
    private const string LocalCategory = nameof(UiText.SettingsLocalTranslation);

    [LocalizedCategory(Category)]
    [LocalizedDisplayName(nameof(UiText.SettingsTranslateTooltips))]
    [LocalizedDescription(nameof(UiText.SettingsTranslateTooltipsDescription))]
    [DefaultValue(true)]
    public bool TranslateTooltips { get; set; } = true;

    [LocalizedCategory(Category)]
    [LocalizedDisplayName(nameof(UiText.SettingsTargetLanguage))]
    [LocalizedDescription(nameof(UiText.SettingsTargetLanguageDescription))]
    [DefaultValue(TargetLanguages.FollowVisualStudio)]
    [TypeConverter(typeof(LanguageChoiceConverter))]
    public string TargetLanguage { get; set; } = TargetLanguages.FollowVisualStudio;

    [LocalizedCategory(Category)]
    [LocalizedDisplayName(nameof(UiText.SettingsTranslateErrors))]
    [LocalizedDescription(nameof(UiText.SettingsTranslateErrorsDescription))]
    [DefaultValue(true)]
    public bool TranslateErrors { get; set; } = true;

    [LocalizedCategory(LocalCategory)]
    [LocalizedDisplayName(nameof(UiText.SettingsModel))]
    [LocalizedDescription(nameof(UiText.SettingsModelDescription))]
    [DefaultValue(ModelChoiceConverter.Automatic)]
    [TypeConverter(typeof(ModelChoiceConverter))]
    public string Model { get; set; } = ModelChoiceConverter.Automatic;

    [LocalizedCategory(LocalCategory)]
    [LocalizedDisplayName(nameof(UiText.SettingsUseGraphicsCard))]
    [LocalizedDescription(nameof(UiText.SettingsUseGraphicsCardDescription))]
    [DefaultValue(true)]
    public bool UseGraphicsCard { get; set; } = true;
}