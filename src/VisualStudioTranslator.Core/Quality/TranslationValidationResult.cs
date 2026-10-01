namespace VisualStudioTranslator.Core.Quality;

/// <summary>What a <see cref="ValidationIssue"/> found wrong with a translated segment.</summary>
public enum ValidationIssueKind
{
    /// <summary>The translation is empty or whitespace although the source has content.</summary>
    EmptyTranslation,

    /// <summary>A placeholder token present in the source is absent from the translation.</summary>
    MissingPlaceholder,

    /// <summary>The translation contains a placeholder token the source never had.</summary>
    UnexpectedPlaceholder,

    /// <summary>A placeholder token appears more often in the translation than in the source.</summary>
    DuplicatedPlaceholder,

    /// <summary>
    /// Start/end wrapper markers are not properly nested. <see cref="MarkupProtector.Restore"/>
    /// would drop or re-close them, silently changing the formatting.
    /// </summary>
    UnbalancedWrapper,

    /// <summary>The translation is much shorter than the source - typically cut off or a refusal.</summary>
    TranslationTooShort,

    /// <summary>The translation is much longer than the source - typically degenerate repetition.</summary>
    TranslationTooLong,

    /// <summary>Too few of the translation's letters belong to the target language's script.</summary>
    WrongScript,

    /// <summary>The provider handed the source back unchanged.</summary>
    IdenticalToSource,

    /// <summary>
    /// Control characters, the Unicode replacement character or unpaired surrogates that the
    /// source did not contain.
    /// </summary>
    InvalidCharacters,

    /// <summary>The source ends like a sentence but the translation does not.</summary>
    PossiblyTruncated,
}

/// <summary>How much a <see cref="ValidationIssue"/> should weigh in deciding what to do with a translation.</summary>
public enum ValidationSeverity
{
    /// <summary>The translation is usable but suspicious; worth logging, not worth discarding.</summary>
    Warning,

    /// <summary>The translation should not be shown or cached as-is.</summary>
    Error,
}

/// <summary>
/// One problem found by <see cref="TranslationValidator"/>. <see cref="Detail"/> only ever
/// carries placeholder tokens and numbers, never source or translated text, so an issue is
/// safe to log and cannot leak what the user is reading.
/// </summary>
public sealed record ValidationIssue
{
    public required ValidationIssueKind Kind { get; init; }

    public string? Detail { get; init; }

    /// <summary>
    /// Derived from <see cref="Kind"/> rather than stored, so a caller can never build an
    /// issue whose severity disagrees with its kind.
    /// </summary>
    public ValidationSeverity Severity =>
        Kind == ValidationIssueKind.PossiblyTruncated ? ValidationSeverity.Warning : ValidationSeverity.Error;
}

/// <summary>
/// The outcome of validating one translated segment. What to do about a failure - retry,
/// fall back to another provider, or show the original text - is the orchestrator's
/// decision; this type only reports what was found.
/// </summary>
public sealed record TranslationValidationResult
{
    public IReadOnlyList<ValidationIssue> Issues { get; init; } = [];

    /// <summary>
    /// <see langword="true"/> when no <see cref="ValidationSeverity.Error"/> issue was found.
    /// Warnings do not make a translation invalid.
    /// </summary>
    public bool IsValid => Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}