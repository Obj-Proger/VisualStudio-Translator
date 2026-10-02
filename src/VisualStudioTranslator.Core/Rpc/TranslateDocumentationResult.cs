using VisualStudioTranslator.Core.Providers.Abstractions;

namespace VisualStudioTranslator.Core.Rpc;

/// <summary>
/// Why a translation request ended the way it did. Sent as a number, so values are never
/// renumbered or reused: add new ones at the end.
/// </summary>
public enum TranslationOutcome
{
    /// <summary>
    /// A provider was asked. This does not mean everything was translated; the counts in
    /// <see cref="TranslateDocumentationResult"/> say how much was.
    /// </summary>
    Completed = 0,

    /// <summary>The source or target language tag is not valid. Nothing was translated.</summary>
    InvalidLanguage = 1,

    /// <summary>No registered provider can translate between these languages.</summary>
    NoProviderAvailable = 2,

    /// <summary>
    /// Only a cloud provider can do it and the user has not agreed to that. The client can
    /// ask, then repeat the request with consent given.
    /// </summary>
    CloudConsentRequired = 3,

    /// <summary>The provider failed; <see cref="TranslateDocumentationResult.ProviderFailure"/> says how, when it is known.</summary>
    ProviderFailed = 4,
}

/// <summary>
/// The answer to a <see cref="TranslateDocumentationRequest"/>. The document is always
/// present and always usable: a part that could not be translated simply keeps its original
/// text.
/// </summary>
public sealed record TranslateDocumentationResult
{
    /// <summary>
    /// The documentation as XML that <c>DocumentationXmlParser</c> reads back. Only the
    /// content is carried; attributes of the original root element are not.
    /// </summary>
    public required string DocumentationXml { get; init; }

    public required TranslationOutcome Outcome { get; init; }

    /// <summary>How many translation units the comment has.</summary>
    public required int SegmentCount { get; init; }

    /// <summary>Units whose translation came from the cache.</summary>
    public required int CachedCount { get; init; }

    /// <summary>Units translated by a provider in this call.</summary>
    public required int TranslatedCount { get; init; }

    /// <summary>
    /// Units left in their original language: nothing to translate in them, no provider
    /// was available, or the result did not pass validation.
    /// </summary>
    public required int UnchangedCount { get; init; }

    /// <summary>Set when <see cref="Outcome"/> is <see cref="TranslationOutcome.ProviderFailed"/> and the failure was a recognized kind.</summary>
    public ProviderFailureKind? ProviderFailure { get; init; }
}