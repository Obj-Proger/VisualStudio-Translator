using System.Globalization;
using VisualStudioTranslator.Core.Languages;

namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>
/// The instruction sent to the language model with every segment.
/// <para>
/// It is general on purpose: no word lists, no rules for a particular language, no examples. A new target
/// language works because the model knows it, not because someone described it here, and the names of the
/// languages come from the operating system's culture data for the same reason.
/// </para>
/// </summary>
internal static class TranslationPrompt
{
    /// <summary>
    /// Goes into the provider's revision. Change it with any change to the wording: the same text can then
    /// translate differently, and what was cached under the old wording must not be served as current.
    /// </summary>
    public const string Version = "1";

    /// <param name="text">The segment, with its markup already written as tags (see <see cref="MarkupTags"/>).</param>
    public static string Build(LanguagePair pair, string text)
    {
        string source = LanguageName(pair.Source);
        string target = LanguageName(pair.Target);

        return
            $"You are a professional {source} ({pair.Source}) to {target} ({pair.Target}) translator of software documentation. " +
            $"Translate the text that follows the line \"Text:\" into {target}.\n\n" +
            "Rules:\n" +
            "- Translate faithfully and completely. Do not omit, add or explain anything.\n" +
            "- Do not translate code: identifiers, names of types, members and parameters, and keywords such as true, false " +
            "and null stay exactly as written.\n" +
            "- Tags such as <x0/>, <g1> and </g1> stand for code and formatting taken out of the text. Copy every tag into " +
            "the translation unchanged, exactly once. <gN> and </gN> are an opening and a closing tag around a phrase: " +
            "translate the phrase and keep both tags around its translation.\n" +
            $"- Output only the {target} translation, with no notes and no added quotes or formatting.\n\n" +
            "Text:\n" + text;
    }

    // "Russian" for "ru", "Portuguese (Brazil)" for "pt-BR". A tag the system has no name for is used as it is.
    private static string LanguageName(string tag)
    {
        try
        {
            string name = CultureInfo.GetCultureInfo(tag).EnglishName;

            return name.Length > 0 && !name.StartsWith("Unknown", StringComparison.Ordinal) ? name : tag;
        }
        catch (CultureNotFoundException)
        {
            return tag;
        }
    }
}