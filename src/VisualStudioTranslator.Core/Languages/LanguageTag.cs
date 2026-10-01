namespace VisualStudioTranslator.Core.Languages;

/// <summary>
/// Canonicalizes BCP 47 language tags ("PT_br" becomes "pt-BR"). Language codes end up in
/// cache keys and provider requests, so "pt-br" and "pt-BR" must never be two different
/// cache entries. Deliberately a small subset of BCP 47: language, optional script,
/// optional region, then lowercase variants and extensions, which is all the providers
/// and the settings UI deal in. Anything outside that shape is rejected, not repaired.
/// </summary>
public static class LanguageTag
{
    /// <returns>The canonical form, or <see langword="null"/> when <paramref name="tag"/> is not a valid tag.</returns>
    public static string? Normalize(string? tag)
    {
        if (tag is null)
        {
            return null;
        }

        string[] subtags = tag.Trim().Replace('_', '-').Split('-');
        string[] result = new string[subtags.Length];
        bool hasScript = false;

        for (int i = 0; i < subtags.Length; i++)
        {
            string subtag = subtags[i];

            if (subtag.Length is 0 or > 8 || !subtag.All(IsAsciiLetterOrDigit))
            {
                return null;
            }

            if (i == 0)
            {
                // The primary language subtag: two or three letters ("en", "fil").
                if (subtag.Length is < 2 or > 3 || !subtag.All(IsAsciiLetter))
                {
                    return null;
                }

                result[i] = subtag.ToLowerInvariant();
            }
            else if (i == 1 && subtag.Length == 4 && subtag.All(IsAsciiLetter))
            {
                // Script: four letters, title case ("Hans", "Latn").
                result[i] = char.ToUpperInvariant(subtag[0]) + subtag[1..].ToLowerInvariant();
                hasScript = true;
            }
            else if (i <= (hasScript ? 2 : 1)
                && ((subtag.Length == 2 && subtag.All(IsAsciiLetter)) || (subtag.Length == 3 && subtag.All(IsAsciiDigit))))
            {
                // Region: two letters or three digits, directly after the language or script ("BR", "419").
                result[i] = subtag.ToUpperInvariant();
            }
            else
            {
                result[i] = subtag.ToLowerInvariant();
            }
        }

        return string.Join("-", result);
    }

    // ASCII on purpose: char.IsLetter would accept Cyrillic or Arabic-Indic digits, which are never valid in a tag.
    private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

    private static bool IsAsciiLetterOrDigit(char c) => IsAsciiLetter(c) || IsAsciiDigit(c);
}