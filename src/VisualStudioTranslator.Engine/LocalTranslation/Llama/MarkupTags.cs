using System.Text.RegularExpressions;

namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>
/// Rewrites the pipeline's placeholder tokens as XML-like tags for a language model, and back.
/// <para>
/// The tokens (<c>⟦0⟧</c>, <c>⟦1:o⟧</c>, <c>⟦1:c⟧</c>) are built from characters a model has rarely seen.
/// Measured on Gemma 4, it dropped or misplaced them, or left the words between a pair untranslated. Tags of
/// the kind localization formats use (<c>&lt;x0/&gt;</c> for a stand-alone token, <c>&lt;g1&gt;</c> and
/// <c>&lt;/g1&gt;</c> around a phrase) came back intact and around the right words. So the tokens stay the
/// pipeline's own notation, and only what this provider puts on the wire differs.
/// </para>
/// </summary>
internal static partial class MarkupTags
{
    [GeneratedRegex("⟦([0-9]+)(:[oc])?⟧")]
    private static partial Regex TokenPattern();

    // Exactly the three shapes ToTags writes, plus the one space some models put before "/>". The space before a
    // stand-alone tag is captured so that it can go with the tag when the tag is dropped.
    [GeneratedRegex("( ?)<x([0-9]+) ?/>|<g([0-9]+)>|</g([0-9]+)>")]
    private static partial Regex TagPattern();

    // Anything that could be taken for one of the tags, whatever its spacing.
    [GeneratedRegex(@"</?\s*[xg][0-9]+\s*/?>")]
    private static partial Regex TagLookalikePattern();

    /// <summary>
    /// Whether <paramref name="text"/> can be sent with tags. It cannot when it already contains something
    /// shaped like one: after translation that could not be told apart from a tag standing for a token.
    /// </summary>
    public static bool CanCarry(string text) => !TagLookalikePattern().IsMatch(text);

    public static string ToTags(string text) =>
        TokenPattern().Replace(text, static match =>
        {
            string id = match.Groups[1].Value;

            return match.Groups[2].Value switch
            {
                ":o" => "<g" + id + ">",
                ":c" => "</g" + id + ">",
                _ => "<x" + id + "/>",
            };
        });

    /// <summary>Whether <paramref name="text"/> holds any of the tags <see cref="ToTags"/> writes.</summary>
    public static bool HasTags(string text) => TagPattern().IsMatch(text);

    /// <summary>
    /// Turns the tags in a model's answer back into tokens.
    /// <list type="bullet">
    /// <item>A tag in any other shape than the ones <see cref="ToTags"/> writes is left as text: the token it
    /// should have been is then missing, which the validator notices, and that is safer than guessing what the
    /// model meant.</item>
    /// <item>A tag the text that was sent did not have is dropped. Models put tags of their own around a quoted
    /// phrase or a term, having been told what tags look like; such a tag stands for nothing, so the words it
    /// wraps are the translation, and keeping it would only get a good translation rejected.</item>
    /// </list>
    /// </summary>
    /// <param name="answer">The model's answer.</param>
    /// <param name="sent">The text as it was sent, with its tags.</param>
    public static string ToTokens(string answer, string sent)
    {
        HashSet<string> known = new(StringComparer.Ordinal);

        foreach (Match tag in TagPattern().Matches(sent))
        {
            known.Add(KeyOf(tag));
        }

        return TagPattern().Replace(answer, match =>
        {
            if (match.Groups[2].Success)
            {
                string space = match.Groups[1].Value;

                if (known.Contains(KeyOf(match)))
                {
                    return space + "⟦" + match.Groups[2].Value + "⟧";
                }

                // Dropped between two words, it takes one of the two spaces around it with it.
                int next = match.Index + match.Length;
                bool spaceFollows =
                    next >= answer.Length || char.IsWhiteSpace(answer[next]) || char.IsPunctuation(answer[next]);

                return spaceFollows ? string.Empty : space;
            }

            if (!known.Contains(KeyOf(match)))
            {
                return string.Empty;
            }

            string id = (match.Groups[3].Success ? match.Groups[3] : match.Groups[4]).Value;

            return match.Groups[3].Success ? "⟦" + id + ":o⟧" : "⟦" + id + ":c⟧";
        });
    }

    // "x0" for a stand-alone tag, "g1" for either tag of a pair: what has to match between what was sent and what came back.
    private static string KeyOf(Match tag) =>
        tag.Groups[2].Success ? "x" + tag.Groups[2].Value : "g" + (tag.Groups[3].Success ? tag.Groups[3] : tag.Groups[4]).Value;
}