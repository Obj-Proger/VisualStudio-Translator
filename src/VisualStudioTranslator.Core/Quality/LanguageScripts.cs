using VisualStudioTranslator.Core.Languages;

namespace VisualStudioTranslator.Core.Quality;

/// <summary>
/// Which <see cref="WritingScript"/>s a translation into a given language is written in.
/// Only languages whose script <see cref="WritingScript"/> knows are listed; everything else,
/// Latin-script languages included, maps to an empty set, which switches the script check
/// off. Skipping a check is the safe failure here: guessing wrong would reject good translations.
/// </summary>
internal static class LanguageScripts
{
    private static readonly WritingScript[] None = [];

    private static readonly Dictionary<string, WritingScript[]> ByLanguage = BuildByLanguage();

    private static readonly Dictionary<string, WritingScript[]> ByScript = BuildByScript();

    public static IReadOnlyCollection<WritingScript> For(string? languageTag)
    {
        string? tag = LanguageTag.Normalize(languageTag);

        if (tag is null)
        {
            return None;
        }

        string[] subtags = tag.Split('-');

        // An explicit script subtag overrides what the language usually uses:
        // "sr" is Cyrillic by default, "sr-Latn" is not.
        if (subtags.Length > 1 && subtags[1].Length == 4)
        {
            return Lookup(ByScript, subtags[1]);
        }

        return Lookup(ByLanguage, subtags[0]);
    }

    private static WritingScript[] Lookup(Dictionary<string, WritingScript[]> table, string key) =>
        table.TryGetValue(key, out WritingScript[] scripts) ? scripts : None;

    private static Dictionary<string, WritingScript[]> BuildByLanguage()
    {
        Dictionary<string, WritingScript[]> table = [];

        void Add(WritingScript[] scripts, params string[] languages)
        {
            foreach (string language in languages)
            {
                table[language] = scripts;
            }
        }

        Add([WritingScript.Cyrillic], "ru", "uk", "be", "bg", "sr", "mk", "kk", "ky", "tg", "mn");
        Add([WritingScript.Greek], "el");
        Add([WritingScript.Arabic], "ar", "fa", "ur", "ps", "sd", "ug");
        Add([WritingScript.Hebrew], "he", "yi");
        Add([WritingScript.Devanagari], "hi", "mr", "ne", "sa");
        Add([WritingScript.Thai], "th");
        Add([WritingScript.Han], "zh", "yue");
        Add([WritingScript.Han, WritingScript.Hiragana, WritingScript.Katakana], "ja");
        Add([WritingScript.Hangul], "ko");

        return table;
    }

    private static Dictionary<string, WritingScript[]> BuildByScript()
    {
        // Keys are ISO 15924 codes in the title case LanguageTag.Normalize produces.
        return new Dictionary<string, WritingScript[]>
        {
            ["Latn"] = [],
            ["Cyrl"] = [WritingScript.Cyrillic],
            ["Grek"] = [WritingScript.Greek],
            ["Arab"] = [WritingScript.Arabic],
            ["Hebr"] = [WritingScript.Hebrew],
            ["Deva"] = [WritingScript.Devanagari],
            ["Thai"] = [WritingScript.Thai],
            ["Hans"] = [WritingScript.Han],
            ["Hant"] = [WritingScript.Han],
            ["Hani"] = [WritingScript.Han],
            ["Jpan"] = [WritingScript.Han, WritingScript.Hiragana, WritingScript.Katakana],
            ["Kore"] = [WritingScript.Hangul],
            ["Hira"] = [WritingScript.Hiragana],
            ["Kana"] = [WritingScript.Katakana],
            ["Hang"] = [WritingScript.Hangul],
        };
    }
}