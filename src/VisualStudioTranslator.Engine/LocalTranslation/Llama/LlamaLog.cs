using Microsoft.Extensions.Logging;

namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>Source-generated log messages for the model server. None carries translated text.</summary>
internal static partial class LlamaLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "The model server is ready, {Seconds:F1} s after it was started.")]
    public static partial void ServerReady(this ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The model server had stopped by itself; starting it again.")]
    public static partial void ServerExited(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The model server could not be started.")]
    public static partial void ServerStartFailed(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The model server did not become ready and was stopped.")]
    public static partial void ServerNotReady(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped the model server.")]
    public static partial void ServerStopped(this ILogger logger);
}