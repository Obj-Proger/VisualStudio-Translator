using Microsoft.Extensions.Logging;

namespace VisualStudioTranslator.Engine.Caching;

/// <summary>Source-generated log messages for the persistent cache. None carries any translated text.</summary>
internal static partial class CacheLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "The translation cache file was unusable and has been recreated.")]
    public static partial void PersistentCacheRecreated(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The persistent translation cache failed during a {Operation}; continuing without it.")]
    public static partial void PersistentCacheFailed(this ILogger logger, string operation, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The persistent translation cache keeps failing and is turned off; translations are cached in memory only until the Engine restarts.")]
    public static partial void PersistentCacheDisabled(this ILogger logger);
}