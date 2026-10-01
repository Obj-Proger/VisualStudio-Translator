using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;

namespace VisualStudioTranslator.Engine.Translation;

/// <summary>
/// Source-generated log messages for the translation pipeline. No message ever carries the
/// text being translated: only segment ids, issue kinds and numbers.
/// </summary>
internal static partial class TranslationLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Provider '{ProviderId}' failed with {FailureKind}; the remaining segments keep their original text.")]
    public static partial void ProviderFailed(this ILogger logger, string providerId, ProviderFailureKind failureKind, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Provider '{ProviderId}' threw an exception that is not a TranslationProviderException; the remaining segments keep their original text.")]
    public static partial void ProviderBrokeContract(this ILogger logger, string providerId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Provider '{ProviderId}' returned {Actual} results for {Expected} segments; the remaining segments keep their original text.")]
    public static partial void ProviderReturnedWrongCount(this ILogger logger, string providerId, int expected, int actual);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The translation of segment {SegmentId} was rejected and its original text is kept: {Issues}.")]
    public static partial void TranslationRejected(this ILogger logger, int segmentId, string issues);

    [LoggerMessage(Level = LogLevel.Information, Message = "Segment {SegmentId} has {Length} characters, over the request limit of {Limit}; it is left untranslated.")]
    public static partial void SegmentTooLarge(this ILogger logger, int segmentId, int length, int limit);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Provider '{ProviderId}' sends text to a remote service and the user has not allowed that; nothing was sent.")]
    public static partial void CloudConsentMissing(this ILogger logger, string providerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Provider '{ProviderId}' does not support {LanguagePair}.")]
    public static partial void LanguagePairUnsupported(this ILogger logger, string providerId, LanguagePair languagePair);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The translation cache failed during a {Operation}; continuing without it.")]
    public static partial void CacheFailed(this ILogger logger, string operation, Exception exception);
}