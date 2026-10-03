using System;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VisualStudioTranslator.Core.Rpc;

namespace VisualStudioTranslator.Vsix.Service;

/// <summary>
/// The one shared connection to the Engine. It is created on first use, started if the Engine
/// is not already running, and dropped and rebuilt when a call shows it has broken, so callers
/// just ask for the service and never think about the process or the pipe.
/// <para>
/// A running Engine is looked for first, with a short wait: launching a second one while the
/// first is up would at best waste memory. The single-instance guarantee itself, and idle
/// shutdown, belong to the Engine's lifecycle work, which is still to come.
/// </para>
/// </summary>
internal sealed class EngineConnection
{
    private static readonly TimeSpan RunningEngineTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan StartedEngineTimeout = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _connecting = new SemaphoreSlim(1, 1);
    private readonly object _sync = new object();
    private ServiceClient? _client;

    public static EngineConnection Shared { get; } = new EngineConnection();

    /// <summary>Starts the Engine and connects in the background, so the first hover does not pay for it.</summary>
    public void WarmUp()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await GetServiceAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(EngineConnection), $"Warm-up failed: {ex}");
            }
        });
    }

    public async Task<ITranslatorService> GetServiceAsync(CancellationToken cancellationToken)
    {
        ServiceClient? current;
        lock (_sync)
        {
            current = _client;
        }

        if (current != null)
        {
            return current.Service;
        }

        await _connecting.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Someone else may have connected while this call was waiting its turn.
            lock (_sync)
            {
                current = _client;
            }

            if (current != null)
            {
                return current.Service;
            }

            ServiceClient connected = await ConnectAsync(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                _client = connected;
            }

            return connected.Service;
        }
        finally
        {
            _connecting.Release();
        }
    }

    /// <summary>
    /// Call when a request through <paramref name="failedService"/> failed in a way that suggests
    /// the connection is gone. The next request connects afresh. A service that has already
    /// been replaced is ignored, so a late failure cannot discard a healthy new connection.
    /// </summary>
    public void ReportFailure(ITranslatorService failedService)
    {
        ServiceClient? toDispose = null;

        lock (_sync)
        {
            if (_client != null && ReferenceEquals(_client.Service, failedService))
            {
                toDispose = _client;
                _client = null;
            }
        }

        toDispose?.Dispose();
    }

    private static async Task<ServiceClient> ConnectAsync(CancellationToken cancellationToken)
    {
        string? userSid = WindowsIdentity.GetCurrent().User?.Value;
        string pipeName = PipeNaming.GetPipeName(userSid ?? "unknown");

        ServiceClient? client = await TryConnectAsync(pipeName, RunningEngineTimeout, cancellationToken).ConfigureAwait(false);

        if (client is null)
        {
            // Disposing the Process object releases the handle; it does not stop the Engine.
            using (ServiceLauncher.Start())
            {
            }

            client = await ServiceClient.ConnectAsync(pipeName, StartedEngineTimeout, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            ClientInfo clientInfo = new ClientInfo
            {
                ProductName = "Visual Studio",
                VisualStudioVersion = "unknown",
                ExtensionVersion = typeof(EngineConnection).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                ProtocolMajor = ProtocolVersion.Major,
                ProtocolMinor = ProtocolVersion.Minor,
            };

            ServiceInfo serviceInfo = await client.Service.HandshakeAsync(clientInfo, cancellationToken).ConfigureAwait(false);

            if (!ProtocolVersion.IsCompatibleWith(serviceInfo.ProtocolMajor))
            {
                throw new InvalidOperationException(
                    $"The Engine speaks protocol {serviceInfo.ProtocolMajor}.{serviceInfo.ProtocolMinor}, which this extension cannot use.");
            }

            ActivityLog.LogInformation(
                nameof(EngineConnection),
                $"Connected to the Engine. Service version {serviceInfo.ServiceVersion}, protocol {serviceInfo.ProtocolMajor}.{serviceInfo.ProtocolMinor}.");

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<ServiceClient?> TryConnectAsync(string pipeName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            return await ServiceClient.ConnectAsync(pipeName, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}