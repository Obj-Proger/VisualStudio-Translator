using System.ComponentModel;
using Microsoft.VisualStudio.Shell;
using VisualStudioTranslator.Core.Languages;

namespace VisualStudioTranslator.Vsix.Options;

/// <summary>
/// The settings page for a Visual Studio that has the new settings experience (Unified Settings) turned off,
/// as Visual Studio 2022 has by default. Where it is on, Visual Studio hides this page and shows the same
/// settings from <c>Options\registration.json</c> instead; <see cref="TranslatorOptions"/> reads whichever is in use.
/// </summary>
public sealed class GeneralOptionsPage : DialogPage
{
    private const string Category = "General";

    [Category(Category)]
    [DisplayName("Translate documentation in tooltips")]
    [Description("Adds a translation of the documentation below the original when you hover over a symbol.")]
    [DefaultValue(true)]
    public bool TranslateTooltips { get; set; } = true;

    [Category(Category)]
    [DisplayName("Translate into")]
    [Description("The language documentation is translated into. By default, the language Visual Studio is shown in.")]
    [DefaultValue(TargetLanguages.FollowVisualStudio)]
    [TypeConverter(typeof(LanguageChoiceConverter))]
    public string TargetLanguage { get; set; } = TargetLanguages.FollowVisualStudio;
}