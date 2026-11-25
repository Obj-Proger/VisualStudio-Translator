using System.ComponentModel;
using System.Globalization;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Vsix.Resources;

namespace VisualStudioTranslator.Vsix.Options;

/// <summary>
/// Shows the language setting of <see cref="GeneralOptionsPage"/> as a list of languages by their own names,
/// while what is stored stays the language's tag.
/// </summary>
internal sealed class LanguageChoiceConverter : StringConverter
{
    private static string FollowVisualStudioLabel => UiText.SettingsSameAsVisualStudio;

    public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => true;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context) =>
        new(new[] { TargetLanguages.FollowVisualStudio }.Concat(TargetLanguages.Choices).ToArray());

    public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    // Only the settings grid, which passes a context, sees the names. Saving passes none and stores the tag, so
    // what is stored does not depend on how a language happens to be named.
    public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType) =>
        context is not null && destinationType == typeof(string) && value is string tag
            ? LabelOf(tag)
            : base.ConvertTo(context, culture, value, destinationType);

    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value) =>
        value is string text ? TagOf(text) : base.ConvertFrom(context, culture, value);

    private static string LabelOf(string tag) =>
        tag == TargetLanguages.FollowVisualStudio ? FollowVisualStudioLabel : TargetLanguages.NativeName(tag);

    private static string TagOf(string text)
    {
        if (text == FollowVisualStudioLabel)
        {
            return TargetLanguages.FollowVisualStudio;
        }

        return TargetLanguages.Choices.FirstOrDefault(tag => TargetLanguages.NativeName(tag) == text) ?? text;
    }
}