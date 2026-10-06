using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VisualStudioTranslator.Engine.Lifecycle;

/// <summary>
/// Ends the Engine when it has had no client for long enough. Without it the Engine, started
/// by the extension, would outlive Visual Studio indefinitely, holding memory and locking its own
/// files so the next build cannot replace them.
/// </summary>
internal sealed class IdleShutdownService(
    ClientTracker clients,
    IHostApplicationLifetime lifetime,
    EngineLifetimeOptions options,
    TimeProvider time,
    ILogger<IdleShutdownService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.EngineStarted(Environment.ProcessId, options.IdleTimeout.TotalSeconds);

        using PeriodicTimer timer = new(options.PollInterval, time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                TimeSpan idle = clients.IdleTime;

                if (idle >= options.IdleTimeout)
                {
                    logger.IdleShutdown(idle.TotalSeconds);
                    lifetime.StopApplication();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The host is stopping for its own reasons; there is nothing left to watch for.
        }
    }
}