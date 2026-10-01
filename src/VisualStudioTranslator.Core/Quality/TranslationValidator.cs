using System.Globalization;
using System.Text;

namespace VisualStudioTranslator.Core.Quality;

/// <summary>
/// Checks what a provider returned for a <see cref="ProtectedSegment"/> <em>before</em>
/// <see cref="MarkupProtector.Restore"/> turns it back into inline content. The order
/// matters: Restore never throws and silently drops whatever it cannot place, so after it
/// has run a lost code span or a broken emphasis is indistinguishable from a good
/// translation. This is the last point where the damage is still visible.
/// <para>
/// Five checks live behind the single <see cref="Validate"/> entry point - placeholder
/// integrity, length ratio, target script, identity with the source, and basic sanity
/// (empty, invalid characters, truncation). The orchestrator needs one verdict per
/// segment, not five calls to combine, and each check is a few lines. Like the rest of
/// this namespace nothing here throws: a bad translation is a result, not an exception.
/// </para>
/// </summary>
public static class TranslationValidator
{
    // Characters that end a sentence in the scripts likely to be a source or a target:
    // Latin punctuation, the ellipsis, the colon, and the CJK, fullwidth, Arabic and
    // Devanagari counterparts. A full stop becomes U+3002 in Chinese and Japanese, so
    // comparing "ends with '.'" on both sides would flag every CJK translation.
    private const string SentenceTerminators = ".!?…:。！？：؟।";

    /// <param name="source">The segment as it was sent to the provider, placeholders included.</param>
    /// <param name="translatedText">The provider's answer, still containing placeholder tokens.</param>
    /// <param name="options">Defaults to <c>new TranslationValidationOptions()</c> when <see langword="null"/>.</param>
    public static TranslationValidationResult Validate(
        ProtectedSegment source, string translatedText, TranslationValidationOptions? options = null)
    {
        options ??= new TranslationValidationOptions();
        List<ValidationIssue> issues = [];

        if (string.IsNullOrWhiteSpace(translatedText))
        {
            // Nothing else is worth reporting on an empty answer: every placeholder would
            // also show up as missing, which is the same problem said many times.
            if (!string.IsNullOrWhiteSpace(source.Text))
            {
                issues.Add(new ValidationIssue { Kind = ValidationIssueKind.EmptyTranslation });
            }

            return new TranslationValidationResult { Issues = issues };
        }

        CheckCharacters(source.Text, translatedText, issues);
        CheckPlaceholders(source.Text, translatedText, issues);
        CheckTerminalPunctuation(source.Text, translatedText, issues);
        CheckStatistics(source.Text, translatedText, options, issues);

        return new TranslationValidationResult { Issues = issues };
    }

    private static void CheckCharacters(string source, string translation, List<ValidationIssue> issues)
    {
        // Only a translation-side problem counts: a source that already contains a
        // replacement character (say, from a mis-decoded file) will pass it straight through.
        if (ContainsInvalidCharacters(translation) && !ContainsInvalidCharacters(source))
        {
            issues.Add(new ValidationIssue { Kind = ValidationIssueKind.InvalidCharacters });
        }
    }

    private static bool ContainsInvalidCharacters(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;
                    continue;
                }

                return true;
            }

            // Tab and line breaks are ordinary whitespace inside prose; other control
            // characters never are.
            if (char.IsLowSurrogate(c) || c == '\uFFFD' || (char.IsControl(c) && c is not ('\t' or '\n' or '\r')))
            {
                return true;
            }
        }

        return false;
    }

    private static void CheckPlaceholders(string source, string translation, List<ValidationIssue> issues)
    {
        Dictionary<string, int> expected = CountTokens(source);
        Dictionary<string, int> actual = CountTokens(translation);
        int issuesBefore = issues.Count;

        foreach (KeyValuePair<string, int> entry in expected)
        {
            actual.TryGetValue(entry.Key, out int found);

            if (found < entry.Value)
            {
                issues.Add(new ValidationIssue { Kind = ValidationIssueKind.MissingPlaceholder, Detail = entry.Key });
            }
            else if (found > entry.Value)
            {
                issues.Add(new ValidationIssue { Kind = ValidationIssueKind.DuplicatedPlaceholder, Detail = entry.Key });
            }
        }

        foreach (string token in actual.Keys)
        {
            if (!expected.ContainsKey(token))
            {
                issues.Add(new ValidationIssue { Kind = ValidationIssueKind.UnexpectedPlaceholder, Detail = token });
            }
        }

        // Tokens are compared as counts, never by position: word order differs between
        // languages, so a translation may legitimately reorder them. What may not change
        // is how wrapper markers nest. That is only worth checking once the tokens
        // themselves agree; otherwise a lost close marker would be reported twice.
        if (issues.Count == issuesBefore && !IsProperlyNested(translation))
        {
            issues.Add(new ValidationIssue { Kind = ValidationIssueKind.UnbalancedWrapper });
        }
    }

    private static Dictionary<string, int> CountTokens(string text)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);

        foreach ((string chunk, bool isPlaceholder) in MarkupProtector.SplitOnPlaceholders(text))
        {
            if (!isPlaceholder)
            {
                continue;
            }

            counts.TryGetValue(chunk, out int count);
            counts[chunk] = count + 1;
        }

        return counts;
    }

    // The same discipline MarkupProtector.Restore applies: a close marker must match the
    // innermost open one, and nothing may stay open at the end. Ids are compared as
    // strings on purpose - the token pattern accepts any Unicode digits of any length, so
    // parsing them to int here would reintroduce the failure modes Restore had to be fixed for.
    private static bool IsProperlyNested(string text)
    {
        Stack<string> open = new();

        foreach ((string chunk, bool isPlaceholder) in MarkupProtector.SplitOnPlaceholders(text))
        {
            if (!isPlaceholder)
            {
                continue;
            }

            // A wrapper token is "⟦" + id + ":o" (or ":c") + "⟧". Dropping the leading
            // bracket and the three trailing characters leaves the id, hence Length - 4.
            if (chunk.EndsWith(":o⟧", StringComparison.Ordinal))
            {
                open.Push(chunk.Substring(1, chunk.Length - 4));
            }
            else if (chunk.EndsWith(":c⟧", StringComparison.Ordinal))
            {
                if (open.Count == 0 || open.Pop() != chunk.Substring(1, chunk.Length - 4))
                {
                    return false;
                }
            }
        }

        return open.Count == 0;
    }

    private static void CheckTerminalPunctuation(string source, string translation, List<ValidationIssue> issues)
    {
        string trimmedSource = source.TrimEnd();
        string trimmedTranslation = translation.TrimEnd();

        // Only a hint: a provider dropping a final full stop is harmless, so this is a
        // warning, and a source ending in a placeholder token is skipped rather than guessed at.
        if (trimmedSource.Length > 0
            && EndsWithTerminator(trimmedSource)
            && !EndsWithTerminator(trimmedTranslation))
        {
            issues.Add(new ValidationIssue { Kind = ValidationIssueKind.PossiblyTruncated });
        }
    }

    private static bool EndsWithTerminator(string text) =>
        SentenceTerminators.IndexOf(text[text.Length - 1]) >= 0;

    private static void CheckStatistics(
        string source, string translation, TranslationValidationOptions options, List<ValidationIssue> issues)
    {
        // Measured on the text outside placeholders: a code span or cref survives
        // translation untouched, so it would only dilute every ratio below.
        string sourceVisible = StripPlaceholders(source);
        (int sourceLetters, _) = CountLetters(sourceVisible, null);

        if (sourceLetters < options.MinLettersForStatisticalChecks)
        {
            return;
        }

        string translationVisible = StripPlaceholders(translation);

        int sourceLength = sourceVisible.Trim().Length;
        if (sourceLength > 0)
        {
            double ratio = (double)translationVisible.Trim().Length / sourceLength;

            if (ratio < options.MinLengthRatio)
            {
                issues.Add(new ValidationIssue { Kind = ValidationIssueKind.TranslationTooShort, Detail = FormatRatio(ratio) });
            }
            else if (ratio > options.MaxLengthRatio)
            {
                issues.Add(new ValidationIssue { Kind = ValidationIssueKind.TranslationTooLong, Detail = FormatRatio(ratio) });
            }
        }

        if (options.TargetScripts.Count > 0 && !options.TargetScripts.Contains(WritingScript.Latin))
        {
            (int letters, int inTargetScripts) = CountLetters(translationVisible, options.TargetScripts);

            if (letters > 0 && (double)inTargetScripts / letters < options.MinTargetScriptRatio)
            {
                issues.Add(new ValidationIssue
                {
                    Kind = ValidationIssueKind.WrongScript,
                    Detail = FormatRatio((double)inTargetScripts / letters),
                });
            }
        }

        if (options.RequireDifferentFromSource
            && string.Equals(source.Trim(), translation.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ValidationIssue { Kind = ValidationIssueKind.IdenticalToSource });
        }
    }

    private static string StripPlaceholders(string text)
    {
        StringBuilder visible = new();

        foreach ((string chunk, bool isPlaceholder) in MarkupProtector.SplitOnPlaceholders(text))
        {
            if (!isPlaceholder)
            {
                visible.Append(chunk);
            }
        }

        return visible.ToString();
    }

    private static (int Letters, int InScripts) CountLetters(string text, IReadOnlyCollection<WritingScript>? scripts)
    {
        int letters = 0;
        int inScripts = 0;

        for (int i = 0; i < text.Length; i++)
        {
            bool isSurrogatePair = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);

            // The (string, int) overload looks at the whole surrogate pair when there is
            // one, so letters outside the BMP (CJK extension blocks) are counted too.
            if (char.IsLetter(text, i))
            {
                letters++;

                if (scripts is not null)
                {
                    int codePoint = isSurrogatePair ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];

                    if (ScriptDetector.Classify(codePoint) is { } script && scripts.Contains(script))
                    {
                        inScripts++;
                    }
                }
            }

            if (isSurrogatePair)
            {
                i++;
            }
        }

        return (letters, inScripts);
    }

    private static string FormatRatio(double ratio) => ratio.ToString("0.00", CultureInfo.InvariantCulture);
}