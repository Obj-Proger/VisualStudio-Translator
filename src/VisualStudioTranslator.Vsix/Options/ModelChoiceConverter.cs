using System.ComponentModel;
using System.Globalization;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Vsix.Resources;

namespace VisualStudioTranslator.Vsix.Options;

/// <summary>
/// Shows the model setting of <see cref="GeneralOptionsPage"/> as the same list the new settings experience shows
/// from <c>Options\registration.json</c>, while what is stored stays the tag (see <see cref="LocalModelSettings.ModelTags"/>).
/// </summary>
internal sealed class ModelChoiceConverter : StringConverter
{
    /// <summary>The tag of <see cref="LocalModelChoice.Automatic"/>, as a constant for the page's default.</summary>
    public const string Automatic = "auto";

    // In the order of LocalModelSettings.ModelTags, the same texts registration.json shows.
    private static string[] Labels =>
        [UiText.SettingsModelAutomatic, UiText.SettingsModelQuality, UiText.SettingsModelLight];

    public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => true;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context) =>
        new(LocalModelSettings.ModelTags.ToArray());

    public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    // As with the language: only the settings grid, which passes a context, sees the labels; saving stores the tag.
    public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType) =>
        context is not null && destinationType == typeof(string) && value is string tag
            ? Labels[(int)LocalModelSettings.ParseModel(tag)]
            : base.ConvertTo(context, culture, value, destinationType);

    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
    {
        if (value is not string text)
        {
            return base.ConvertFrom(context, culture, value);
        }

        int index = Array.IndexOf(Labels, text);

        return index >= 0 ? LocalModelSettings.ModelTags[index] : text;
    }
}