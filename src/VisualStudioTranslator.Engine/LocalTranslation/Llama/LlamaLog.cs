using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Rpc;

namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>Source-generated log messages for the model server. None carries translated text.</summary>
internal static partial class LlamaLog
{
    [LoggerMessage(
        Level = LogLevel.Information, Message = "The model server is ready on {Place}, {Seconds:F1} s after it was started.")]
    public static partial void ServerReady(this ILogger logger, double seconds, string place);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The model server failed on {Place}; trying the next place.")]
    public static partial void GpuUnusable(this ILogger logger, string place, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Graphics cards found: {Count} ({Names}).")]
    public static partial void CardsFound(this ILogger logger, int count, string names);

    [LoggerMessage(Level = LogLevel.Information, Message = "Graphics cards timed, fastest first: {Order}.")]
    public static partial void CardsRanked(this ILogger logger, string order);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The model server had stopped by itself; starting it again.")]
    public static partial void ServerExited(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The model server could not be started.")]
    public static partial void ServerStartFailed(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The model server did not become ready and was stopped.")]
    public static partial void ServerNotReady(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "No translation for {Minutes:F0} min; stopping the model server.")]
    public static partial void ServerIdle(this ILogger logger, double minutes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped the model server.")]
    public static partial void ServerStopped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Installing the model server and {Model}.")]
    public static partial void InstallStarted(this ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Information, Message = "Installed the model server, build {Version}.")]
    public static partial void RuntimeInstalled(this ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Installed {Model}; local translation is ready.")]
    public static partial void Installed(this ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Installing the model server or the model failed.")]
    public static partial void InstallFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Translating locally with {Model} ({Choice}), graphics card {GraphicsCard}, from the next request on.")]
    public static partial void LocalModelChanged(this ILogger logger, string model, LocalModelChoice choice, string graphicsCard);
}