namespace VisualStudioTranslator.Core.Quality;

/// <summary>
/// Tunes <see cref="TranslationValidator"/>. The defaults are deliberately lenient: the
/// statistical checks exist to catch a provider that failed outright (nothing translated,
/// truncated, looping), not to grade style, so a false alarm costs more than a miss.
/// Per-language tuning can be layered on later by whoever builds the options; the
/// validator itself stays language-agnostic.
/// </summary>
public sealed record TranslationValidationOptions
{
    /// <summary>
    /// The scripts the translation is expected to be written in. Empty, or containing
    /// <see cref="WritingScript.Latin"/>, disables the script check: the source is almost
    /// always Latin too, so a Latin-script check could not tell a translation from an
    /// untouched source. Japanese, for instance, is <c>Han + Hiragana + Katakana</c>.
    /// </summary>
    public IReadOnlyCollection<WritingScript> TargetScripts { get; init; } = [];

    /// <summary>
    /// Lowest acceptable translation-to-source length ratio, measured on the text outside
    /// placeholders. English to Chinese can legitimately reach about 0.2, hence the low default.
    /// </summary>
    public double MinLengthRatio { get; init; } = 0.15;

    /// <summary>Highest acceptable translation-to-source length ratio. Real expansion rarely exceeds 2.</summary>
    public double MaxLengthRatio { get; init; } = 4.0;

    /// <summary>
    /// Smallest acceptable share of the translation's letters that belong to
    /// <see cref="TargetScripts"/>. Kept low because technical prose legitimately keeps
    /// Latin identifiers and product names that are not glossary terms.
    /// </summary>
    public double MinTargetScriptRatio { get; init; } = 0.3;

    /// <summary>
    /// Sources with fewer letters than this skip the length, script and identity checks.
    /// Those are statistical heuristics and mean nothing on a couple of words: "Id" is
    /// correctly left as "Id", and "Gets" becomes the much longer "Получает".
    /// </summary>
    public int MinLettersForStatisticalChecks { get; init; } = 8;

    /// <summary>
    /// Report <see cref="ValidationIssueKind.IdenticalToSource"/>. Turn off when identical
    /// output is expected, e.g. the text was already in the target language.
    /// </summary>
    public bool RequireDifferentFromSource { get; init; } = true;

    /// <summary>
    /// The defaults, with <see cref="TargetScripts"/> filled in from the language being
    /// translated into ("ru" gives Cyrillic, "ja" gives Han, Hiragana and Katakana). Latin-script
    /// languages, languages written in a script <see cref="WritingScript"/> does not list,
    /// and invalid tags all get an empty set, so the script check simply does not run.
    /// </summary>
    public static TranslationValidationOptions ForTarget(string? targetLanguage) =>
        new() { TargetScripts = LanguageScripts.For(targetLanguage) };
}