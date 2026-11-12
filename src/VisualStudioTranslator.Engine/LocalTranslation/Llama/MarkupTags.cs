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

    // Exactly the three shapes ToTags writes, plus the one space some models put before "/>".
    [GeneratedRegex("<x([0-9]+) ?/>|<g([0-9]+)>|</g([0-9]+)>")]
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

    /// <summary>
    /// Turns the tags in a model's answer back into tokens. A tag in any other shape than the ones
    /// <see cref="ToTags"/> writes is left as text: the token it should have been is then missing, which the
    /// validator notices, and that is safer than guessing what the model meant.
    /// </summary>
    public static string ToTokens(string text) =>
        TagPattern().Replace(text, static match =>
        {
            if (match.Groups[1].Success)
            {
                return "⟦" + match.Groups[1].Value + "⟧";
            }

            if (match.Groups[2].Success)
            {
                return "⟦" + match.Groups[2].Value + ":o⟧";
            }

            return "⟦" + match.Groups[3].Value + ":c⟧";
        });
}