using System.Globalization;
using System.Text;
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
    public const string Version = "5";

    // The longest piece of code named under "Tags:"; a longer one is cut, since only its gist helps the reading.
    private const int MaxCodeLength = 80;

    /// <param name="text">The segment, with its markup already written as tags (see <see cref="MarkupTags"/>).</param>
    /// <param name="codes">The code each <c>&lt;xN/&gt;</c> tag stands for, by N, so the model knows what the sentence
    /// is about: "a copy of &lt;x0/&gt;" reads otherwise as a copy of anything.</param>
    /// <param name="context">The paragraphs around the segment, to be read and not translated.</param>
    public static string Build(
        LanguagePair pair, string text, IReadOnlyDictionary<int, string>? codes = null, string? context = null)
    {
        string target = LanguageName(pair.Target);
        bool sourceKnown = pair.Source != LanguageTag.Undetermined;

        // With the source unknown the model is told only where to translate to, and that text already there stays.
        string role = sourceKnown
            ? $"You are a professional {LanguageName(pair.Source)} ({pair.Source}) to {target} ({pair.Target}) translator " +
              "of software documentation. "
            : $"You are a professional translator of software documentation into {target} ({pair.Target}). ";
        string alreadyRule = sourceKnown
            ? string.Empty
            : $"- If the text is already written in {target}, output it unchanged.\n";

        // The rule about tags only goes with text that has tags. Told about tags with none in sight, the model
        // made some up, around quoted phrases.
        string tagRule = MarkupTags.HasTags(text)
            ? "- Tags such as <x0/>, <g1> and </g1> stand for code and formatting taken out of the text. Copy every tag into " +
              "the translation unchanged, exactly once. <gN> and </gN> are an opening and a closing tag around a phrase: " +
              "translate the phrase and keep both tags around its translation.\n"
            : string.Empty;

        string legend = Legend(text, codes);
        string legendRule = legend.Length > 0
            ? "- The lines under \"Tags:\" say which code each <xN/> tag stands for. Read the sentence with that code in " +
              "mind, so each word relates to it as in the original, but write the tag itself, not the code.\n"
            : string.Empty;

        bool hasContext = !string.IsNullOrWhiteSpace(context);
        string contextNote = hasContext
            ? "The text is one paragraph of a longer documentation comment. The paragraphs around it are given between " +
              "the lines \"Context:\" and \"End of context.\" only to help understand it: do not translate them.\n\n"
            : string.Empty;

        return
            role +
            $"Translate the text that follows the line \"Text:\" into {target}.\n\n" +
            contextNote +
            "Rules:\n" +
            "- Translate faithfully and completely. Do not omit, add or explain anything.\n" +
            "- Do not translate code: identifiers, names of types, members and parameters, and keywords such as true, false " +
            "and null stay exactly as written.\n" +
            "- The text documents code and is read in a code editor such as Visual Studio. Translate technical words in " +
            $"their programming sense, with the terms programmers writing in {target} use: a run of text is a piece of " +
            "text, not a launch; a tooltip is the editor's popup hint.\n" +
            "- Keep the names of products and components, such as Visual Studio, Quick Info or Engine, as written, and keep " +
            "every subject and object the original sentence has.\n" +
            $"- Write natural, grammatical {target}: words agree in gender, number and case.\n" +
            tagRule +
            legendRule +
            alreadyRule +
            $"- Output only the {target} translation of the text, with no notes and no added quotes or formatting.\n\n" +
            (hasContext ? "Context:\n" + context!.Trim() + "\nEnd of context.\n\n" : string.Empty) +
            legend +
            "Text:\n" + text;
    }

    // "Tags:" and a line for each stand-alone tag the text holds, such as "<x0/> = sourceFolder", or nothing.
    private static string Legend(string text, IReadOnlyDictionary<int, string>? codes)
    {
        if (codes is null || codes.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder legend = new();

        foreach (KeyValuePair<int, string> code in codes.OrderBy(pair => pair.Key))
        {
            string tag = "<x" + code.Key.ToString(CultureInfo.InvariantCulture) + "/>";
            if (!text.Contains(tag, StringComparison.Ordinal))
            {
                continue;
            }

            string written = string.Join(" ", code.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (written.Length > MaxCodeLength)
            {
                written = written[..MaxCodeLength] + "…";
            }

            legend.Append(tag).Append(" = ").Append(written).Append('\n');
        }

        return legend.Length > 0 ? "Tags:\n" + legend + "\n" : string.Empty;
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