using Microsoft.Extensions.Logging;

namespace VisualStudioTranslator.Engine.Lifecycle;

internal static partial class LifecycleLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Engine started (process {ProcessId}); it shuts down after {IdleSeconds:F0} seconds without a connected client.")]
    public static partial void EngineStarted(this ILogger logger, int processId, double idleSeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "No client has been connected for {IdleSeconds:F0} seconds; the Engine is shutting down.")]
    public static partial void IdleShutdown(this ILogger logger, double idleSeconds);
}