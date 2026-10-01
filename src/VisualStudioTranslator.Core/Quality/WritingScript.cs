namespace VisualStudioTranslator.Core.Quality;

/// <summary>
/// A writing system, at the granularity <see cref="TranslationValidator"/> needs to tell a
/// translation from an untouched source. Deliberately not exhaustive: a target language
/// written in a script not listed here simply gets no script check.
/// </summary>
public enum WritingScript
{
    Latin,
    Cyrillic,
    Greek,
    Arabic,
    Hebrew,
    Devanagari,
    Thai,
    Han,
    Hiragana,
    Katakana,
    Hangul,
}

/// <summary>
/// Maps a code point to its <see cref="WritingScript"/>. Hand-rolled because the BCL has no
/// script lookup, and Core targets netstandard2.0, where even <c>System.Text.Rune</c> is
/// unavailable. Only letters are ever classified (callers filter with
/// <see cref="char.IsLetter(string, int)"/> first), so blocks that also hold punctuation
/// or symbols, like Latin-1 Supplement or Katakana, need no per-character exceptions.
/// </summary>
internal static class ScriptDetector
{
    private static readonly (int Start, int End, WritingScript Script)[] Ranges =
    [
        (0x0041, 0x005A, WritingScript.Latin),
        (0x0061, 0x007A, WritingScript.Latin),
        (0x00C0, 0x024F, WritingScript.Latin),
        (0x1E00, 0x1EFF, WritingScript.Latin),

        (0x0370, 0x03FF, WritingScript.Greek),
        (0x1F00, 0x1FFF, WritingScript.Greek),

        (0x0400, 0x052F, WritingScript.Cyrillic),
        (0x1C80, 0x1C8F, WritingScript.Cyrillic),
        (0x2DE0, 0x2DFF, WritingScript.Cyrillic),
        (0xA640, 0xA69F, WritingScript.Cyrillic),

        (0x0590, 0x05FF, WritingScript.Hebrew),
        (0xFB1D, 0xFB4F, WritingScript.Hebrew),

        (0x0600, 0x06FF, WritingScript.Arabic),
        (0x0750, 0x077F, WritingScript.Arabic),
        (0x08A0, 0x08FF, WritingScript.Arabic),
        (0xFB50, 0xFDFF, WritingScript.Arabic),
        (0xFE70, 0xFEFF, WritingScript.Arabic),

        (0x0900, 0x097F, WritingScript.Devanagari),

        (0x0E00, 0x0E7F, WritingScript.Thai),

        (0x1100, 0x11FF, WritingScript.Hangul),
        (0x3130, 0x318F, WritingScript.Hangul),
        (0xA960, 0xA97F, WritingScript.Hangul),
        (0xAC00, 0xD7FF, WritingScript.Hangul),

        (0x3040, 0x309F, WritingScript.Hiragana),

        (0x30A0, 0x30FF, WritingScript.Katakana),
        (0x31F0, 0x31FF, WritingScript.Katakana),
        (0xFF66, 0xFF9F, WritingScript.Katakana),

        (0x3400, 0x4DBF, WritingScript.Han),
        (0x4E00, 0x9FFF, WritingScript.Han),
        (0xF900, 0xFAFF, WritingScript.Han),
        (0x20000, 0x323AF, WritingScript.Han),
    ];

    public static WritingScript? Classify(int codePoint)
    {
        foreach ((int start, int end, WritingScript script) in Ranges)
        {
            if (codePoint >= start && codePoint <= end)
            {
                return script;
            }
        }

        return null;
    }
}