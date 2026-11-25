namespace VisualStudioTranslator.Core.Quality;

/// <summary>
/// Tells from the writing alone whether a text is already in a language, where the writing can tell: for
/// languages written in a script of their own, such as Russian in Cyrillic or Korean in Hangul. A Latin-script
/// language cannot be told from another that way, English from German, so for those the answer is always
/// <see langword="false"/> and the translator, told to leave such text as it is, decides.
/// <para>
/// It saves the model from translating documentation that is already in the user's language: a Russian comment
/// read in a Russian Visual Studio.
/// </para>
/// </summary>
public static class LanguageGuess
{
    // Code left in the text, such as a type name the markup did not mark, is Latin whatever the prose is in, and
    // type names are long: "Возвращает IEnumerable элементов списка" is two thirds Cyrillic. Half the letters
    // still keep an English comment that quotes a word or two of another language from counting as that language.
    private const double MinShareOfLetters = 0.5;

    public static bool IsAlreadyIn(string text, string languageTag)
    {
        IReadOnlyCollection<WritingScript> scripts = LanguageScripts.For(languageTag);

        if (scripts.Count == 0)
        {
            return false;
        }

        int letters = 0;
        int inScripts = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsLetter(text, i))
            {
                continue;
            }

            int codePoint = char.ConvertToUtf32(text, i);

            if (char.IsHighSurrogate(text[i]))
            {
                i++;
            }

            letters++;

            if (ScriptDetector.Classify(codePoint) is { } script && scripts.Contains(script))
            {
                inScripts++;
            }
        }

        return letters > 0 && (double)inScripts / letters >= MinShareOfLetters;
    }
}