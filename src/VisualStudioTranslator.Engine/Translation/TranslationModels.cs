using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Core.Quality;

namespace VisualStudioTranslator.Engine.Translation;

/// <summary>One documentation comment to translate, and the choices that shape how.</summary>
internal sealed record TranslationRequest
{
    public required DocumentModel Document { get; init; }

    public required LanguagePair Languages { get; init; }

    public Glossary Glossary { get; init; } = new();

    /// <summary>
    /// Whether the user has agreed to their text being sent to a remote service. Without it a
    /// <see cref="ProviderKind.Cloud"/> provider is never called, whatever else is configured.
    /// </summary>
    public bool AllowCloudProvider { get; init; }
}

/// <summary>
/// What came out of a translation. <see cref="Document"/> is always usable: a segment that
/// could not be translated simply keeps its original text, so the counts tell how much of
/// the document is actually translated.
/// </summary>
internal sealed record TranslationResult
{
    public required DocumentModel Document { get; init; }

    public required int SegmentCount { get; init; }

    /// <summary>Segments whose translation came from the cache.</summary>
    public required int CachedCount { get; init; }

    /// <summary>Segments translated by the provider in this call.</summary>
    public required int TranslatedCount { get; init; }

    /// <summary>
    /// Segments left as the author wrote them: nothing to translate in them, the provider
    /// failed or was not allowed, or the result did not pass validation.
    /// </summary>
    public required int UnchangedCount { get; init; }

    /// <summary>Why the provider could not do its work, when it threw a recognized failure; the UI can say something useful about it.</summary>
    public ProviderFailureKind? ProviderFailure { get; init; }

    /// <summary>The provider was never called because it would send text to a remote service without the user's consent.</summary>
    public bool BlockedByCloudConsent { get; init; }
}