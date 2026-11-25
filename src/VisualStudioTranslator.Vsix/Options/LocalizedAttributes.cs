using System.ComponentModel;
using VisualStudioTranslator.Vsix.Resources;

namespace VisualStudioTranslator.Vsix.Options;

// The classic settings page names its settings through attributes, which can only hold constants. These name a text
// of UiText instead and show it in the language of Visual Studio, as the new settings experience does with the same
// texts through Options\registration.json.

/// <summary>The name of a setting on the classic page, by the name of its text in UiText.</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class LocalizedDisplayNameAttribute(string displayName) : DisplayNameAttribute(displayName)
{
    public override string DisplayName => UiText.ByName(DisplayNameValue);
}

/// <summary>The description of a setting on the classic page, by the name of its text in UiText.</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class LocalizedDescriptionAttribute(string description) : DescriptionAttribute(description)
{
    public override string Description => UiText.ByName(DescriptionValue);
}

/// <summary>The group of a setting on the classic page, by the name of its text in UiText.</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class LocalizedCategoryAttribute(string category) : CategoryAttribute(category)
{
    protected override string GetLocalizedString(string value) => UiText.ByName(value);
}