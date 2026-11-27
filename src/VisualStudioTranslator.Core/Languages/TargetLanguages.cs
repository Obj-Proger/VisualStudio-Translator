using System.Globalization;

namespace VisualStudioTranslator.Core.Languages;

/// <summary>
/// The languages the user can choose to have documentation translated into, and how a choice becomes the
/// language actually used. The settings page of the extension offers exactly <see cref="Choices"/>.
/// </summary>
public static class TargetLanguages
{
    /// <summary>The choice that follows the language Visual Studio itself is shown in.</summary>
    public const string FollowVisualStudio = "auto";

    /// <summary>
    /// The languages offered, as BCP 47 tags: the languages of Visual Studio's own interface. English is one of
    /// them, for documentation written in another language. Any other is reached through
    /// <see cref="FollowVisualStudio"/>.
    /// </summary>
    public static IReadOnlyList<string> Choices { get; } =
        ["en", "ru", "de", "fr", "es", "it", "pt-BR", "pl", "cs", "tr", "zh-Hans", "ja", "ko"];

    /// <summary>The language to translate into, or <see langword="null"/> when none can be told.</summary>
    /// <param name="choice">What the user chose; empty, unknown or <see cref="FollowVisualStudio"/> follows Visual Studio.</param>
    /// <param name="displayLanguage">The tag of the language Visual Studio is shown in, such as "ru-RU".</param>
    public static string? Resolve(string? choice, string displayLanguage)
    {
        string tag = choice is not null && Choices.Contains(choice, StringComparer.OrdinalIgnoreCase)
            ? choice
            : displayLanguage;

        return tag.Length == 0 ? null : tag;
    }

    /// <summary>
    /// Whether two tags name the same language, whatever the region: "en-US" and "en" do, "pt-BR" and "pt-PT"
    /// do too, since documentation reads the same in both. Translating between them would change nothing.
    /// </summary>
    public static bool IsSameLanguage(string first, string second) =>
        string.Equals(PrimaryOf(first), PrimaryOf(second), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The language's name in itself, as a list of languages shows it, with a capital: "Русский", "Deutsch". Some
    /// languages write their own name in lower case ("русский", "français"), as is right inside a sentence but not at
    /// the head of an item, which is how Windows and Visual Studio list them too.
    /// </summary>
    public static string ListName(string tag)
    {
        string name = NativeName(tag);

        if (name.Length == 0 || !char.IsLower(name[0]))
        {
            return name;
        }

        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(tag);
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.InvariantCulture;
        }

        return culture.TextInfo.ToUpper(name[0]) + name.Substring(1);
    }

    /// <summary>The language's name in itself, as it reads inside a sentence: "русский", "Deutsch".</summary>
    public static string NativeName(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag).NativeName;
        }
        catch (CultureNotFoundException)
        {
            return tag;
        }
    }

    private static string PrimaryOf(string tag)
    {
        int dash = tag.IndexOf('-');
        return dash < 0 ? tag : tag.Substring(0, dash);
    }
}