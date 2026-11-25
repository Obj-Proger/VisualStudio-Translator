using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Core.Quality;

namespace VisualStudioTranslator.Core.Rpc;

/// <summary>
/// Plain text to translate, such as what the user typed into the translator window or a comment selected in the
/// editor. Self-contained like <see cref="TranslateDocumentationRequest"/>.
/// Added in protocol version 1.4.
/// </summary>
public sealed record TranslateTextRequest
{
    /// <summary>The longest text taken: some pages of prose, which a local model takes minutes over.</summary>
    public const int MaxLength = 20_000;

    /// <summary>
    /// The text. Paragraphs are separated by blank lines; the lines within one are joined, as in a wrapped comment
    /// (see <see cref="Documentation.PlainText"/>). Comment markers are the caller's to remove.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>BCP 47 tag of the text's language, or "und" when it is not known.</summary>
    public required string SourceLanguage { get; init; }

    /// <summary>BCP 47 tag of the language to translate into.</summary>
    public required string TargetLanguage { get; init; }

    /// <summary>Terms to keep or to translate in a fixed way. Entries with a blank term are ignored.</summary>
    public IReadOnlyList<GlossaryEntry> Glossary { get; init; } = [];

    /// <summary>Whether the user has agreed to their text being sent to a remote service.</summary>
    public bool AllowCloudProvider { get; init; }
}

/// <summary>The answer to a <see cref="TranslateTextRequest"/>; the counts mean what they do for documentation.</summary>
public sealed record TranslateTextResult
{
    /// <summary>The translation, laid out like the text sent. Parts that could not be translated keep their original text.</summary>
    public required string Text { get; init; }

    public required TranslationOutcome Outcome { get; init; }

    public required int SegmentCount { get; init; }

    public required int CachedCount { get; init; }

    public required int TranslatedCount { get; init; }

    public required int UnchangedCount { get; init; }

    public ProviderFailureKind? ProviderFailure { get; init; }
}