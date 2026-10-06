using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.Lifecycle;

namespace VisualStudioTranslator.Engine.Rpc;

/// <summary>
/// Hosts <see cref="ITranslatorService"/> on a named pipe restricted to the current
/// Windows user, accepting connections from one or more Vsix clients for as long as
/// the Engine process is running. Each connection is reported to <see cref="ClientTracker"/>,
/// which is what lets the Engine notice when the last client has gone.
/// </summary>
internal sealed class NamedPipeRpcServer(
    ITranslatorService service,
    ClientTracker clients,
    ILogger<NamedPipeRpcServer> logger) : BackgroundService
{
    /// <summary>
    /// The name of the pipe this Engine build listens on. Scoped to the current user and
    /// the wire protocol's major version, so an incompatible or stale Engine never
    /// intercepts a connection meant for another one.
    /// </summary>
    public static string PipeName { get; } = BuildPipeName();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.ListeningOnPipe(PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe = CreatePipe();

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                pipe.Dispose();
                break;
            }

            _ = HandleConnectionAsync(pipe, stoppingToken);
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        clients.ClientConnected();

        try
        {
            using (pipe)
            using (JsonRpc jsonRpc = new(pipe, pipe))
            {
                jsonRpc.AddLocalRpcTarget(service);
                jsonRpc.StartListening();

                try
                {
                    await jsonRpc.Completion.WaitAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The Engine is shutting down; the pipe is disposed by the using block above.
                }
                catch (Exception ex)
                {
                    logger.ClientConnectionEndedUnexpectedly(PipeName, ex);
                }
            }
        }
        finally
        {
            clients.ClientDisconnected();
        }
    }

    private static NamedPipeServerStream CreatePipe() =>
        new(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private static string BuildPipeName()
    {
        string? userSid = WindowsIdentity.GetCurrent().User?.Value;
        return PipeNaming.GetPipeName(userSid ?? "unknown");
    }
}