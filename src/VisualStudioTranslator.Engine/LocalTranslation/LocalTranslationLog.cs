using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Languages;

namespace VisualStudioTranslator.Engine.LocalTranslation;

/// <summary>Source-generated log messages for the local provider. None carries translated text.</summary>
internal static partial class LocalTranslationLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded the local translation model for {LanguagePair}.")]
    public static partial void ModelLoaded(this ILogger logger, LanguagePair languagePair);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The local translation model for {LanguagePair} could not be loaded.")]
    public static partial void ModelLoadFailed(this ILogger logger, LanguagePair languagePair, Exception exception);
}