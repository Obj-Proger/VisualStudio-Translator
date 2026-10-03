using System.Text;

namespace VisualStudioTranslator.Core.Quality;

/// <summary>
/// Removes the stray characters a translation model sometimes leaves next to a placeholder,
/// such as a lone "]" after "⟦0⟧" or a second "⟧". The validator cannot see these: it checks
/// that every token is present and nothing more, and a token with a bracket beside it is still
/// a present token. Left alone, the character reaches the reader as a stray "]" in a tooltip.
/// <para>
/// The rule is narrow on purpose, because legitimate brackets are common in documentation. A
/// square bracket is only removed when it touches a placeholder and the translation has more
/// brackets of that kind than the source did; the token's own brackets are removed anywhere
/// outside a token, since they never belong in prose.
/// </para>
/// </summary>
public static class PlaceholderArtifacts
{
    private const char TokenOpen = '⟦';
    private const char TokenClose = '⟧';

    /// <param name="source">The protected text that was sent to the provider.</param>
    /// <param name="translation">What the provider returned for it.</param>
    public static string Clean(string source, string translation)
    {
        List<(string Text, bool IsPlaceholder)> chunks = [.. MarkupProtector.SplitOnPlaceholders(translation)];

        if (chunks.Count == 0)
        {
            return translation;
        }

        string sourceText = TextOutsidePlaceholders(source);
        string translationText = TextOutsidePlaceholders(translation);

        bool sourceHasTokenOpen = sourceText.IndexOf(TokenOpen) >= 0;
        bool sourceHasTokenClose = sourceText.IndexOf(TokenClose) >= 0;
        int excessOpen = Count(translationText, '[') - Count(sourceText, '[');
        int excessClose = Count(translationText, ']') - Count(sourceText, ']');

        StringBuilder cleaned = new(translation.Length);

        for (int i = 0; i < chunks.Count; i++)
        {
            (string text, bool isPlaceholder) = chunks[i];

            if (isPlaceholder)
            {
                cleaned.Append(text);
                continue;
            }

            if (!sourceHasTokenOpen)
            {
                text = text.Replace(TokenOpen.ToString(), string.Empty);
            }

            if (!sourceHasTokenClose)
            {
                text = text.Replace(TokenClose.ToString(), string.Empty);
            }

            if (i > 0 && chunks[i - 1].IsPlaceholder)
            {
                text = TrimLeadingBracket(text, ref excessOpen, ref excessClose);
            }

            if (i < chunks.Count - 1 && chunks[i + 1].IsPlaceholder)
            {
                text = TrimTrailingBracket(text, ref excessOpen, ref excessClose);
            }

            cleaned.Append(text);
        }

        return cleaned.ToString();
    }

    private static string TextOutsidePlaceholders(string text)
    {
        StringBuilder outside = new();

        foreach ((string chunk, bool isPlaceholder) in MarkupProtector.SplitOnPlaceholders(text))
        {
            if (!isPlaceholder)
            {
                outside.Append(chunk);
            }
        }

        return outside.ToString();
    }

    private static int Count(string text, char c)
    {
        int count = 0;

        foreach (char each in text)
        {
            if (each == c)
            {
                count++;
            }
        }

        return count;
    }

    // "⟦0⟧ ], when" -> "⟦0⟧, when": the bracket and the space before it go.
    private static string TrimLeadingBracket(string text, ref int excessOpen, ref int excessClose)
    {
        int index = 0;

        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        if (index < text.Length)
        {
            if (text[index] == '[' && excessOpen > 0)
            {
                excessOpen--;
                return text[(index + 1)..];
            }

            if (text[index] == ']' && excessClose > 0)
            {
                excessClose--;
                return text[(index + 1)..];
            }
        }

        return text;
    }

    // "text [ ⟦0⟧" -> "text ⟦0⟧".
    private static string TrimTrailingBracket(string text, ref int excessOpen, ref int excessClose)
    {
        int end = text.Length;

        while (end > 0 && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        if (end > 0)
        {
            if (text[end - 1] == '[' && excessOpen > 0)
            {
                excessOpen--;
                return text[..(end - 1)];
            }

            if (text[end - 1] == ']' && excessClose > 0)
            {
                excessClose--;
                return text[..(end - 1)];
            }
        }

        return text;
    }
}