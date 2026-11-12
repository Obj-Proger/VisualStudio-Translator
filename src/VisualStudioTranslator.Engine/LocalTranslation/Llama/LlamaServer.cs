using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Providers.Abstractions;

namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>
/// Runs llama.cpp's server as a separate program and keeps the way to reach it.
/// <para>
/// The model runs outside the Engine on purpose. It takes gigabytes of memory, which are only given
/// back when its process ends, and it leans on graphics drivers the Engine has no business crashing
/// with. As a program of its own it can be stopped when idle and started again on the next request.
/// </para>
/// <para>
/// The server listens on this machine only, on a port picked at start, and wants a key that is made
/// up at start and handed over through the environment, where other programs do not see it the way
/// they see a command line. Nothing else on the machine can therefore use it to read what is being
/// translated.
/// </para>
/// <para>
/// <see cref="Client"/> is one object for the life of this class: it finds whichever server is running
/// now. A request made while none is fails as an ordinary network error would.
/// </para>
/// </summary>
internal sealed class LlamaServer : IDisposable
{
    private const string Loopback = "127.0.0.1";
    private const string ApiKeyVariable = "LLAMA_API_KEY";

    private static readonly Uri HealthEndpoint = new("health", UriKind.Relative);

    private readonly IServerProcessLauncher _launcher;
    private readonly LlamaServerOptions _options;
    private readonly ILogger<LlamaServer> _logger;

    // Held by whoever is starting the server, so that callers arriving together start one, not several.
    private readonly SemaphoreSlim _starting = new(1, 1);

    // Guards the fields below. Never held across an await, so stopping never waits for a start.
    private readonly Lock _sync = new();
    private Running? _running;
    private bool _disposed;

    /// <param name="transport">
    /// What carries the requests: the real network in the Engine, a stand-in in tests. Disposed with this object.
    /// </param>
    public LlamaServer(
        IServerProcessLauncher launcher, LlamaServerOptions options, HttpMessageHandler transport, ILogger<LlamaServer> logger)
    {
        _launcher = launcher;
        _options = options;
        _logger = logger;

        Client = new HttpClient(new EndpointHandler(this, transport))
        {
            // Only so that relative addresses resolve; the port is filled in for each request.
            BaseAddress = new UriBuilder(Uri.UriSchemeHttp, Loopback).Uri,
            Timeout = options.RequestTimeout,
        };
    }

    /// <summary>Sends requests to the server that is running at the moment, with its key.</summary>
    public HttpClient Client { get; }

    /// <summary>
    /// Returns once a server is running and answering, starting one if there is none or the last one
    /// died. A caller that gives up waiting does not stop the server: it carries on loading, and the
    /// next caller picks it up where it is.
    /// </summary>
    /// <exception cref="TranslationProviderException">The server could not be started or did not become ready.</exception>
    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        await _starting.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Running running = GetOrLaunch();

            if (running.Ready)
            {
                return;
            }

            try
            {
                await WaitUntilReadyAsync(running, cancellationToken).ConfigureAwait(false);
            }
            catch (TranslationProviderException)
            {
                _logger.ServerNotReady();
                Discard(running);
                throw;
            }

            running.Ready = true;

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.ServerReady(Stopwatch.GetElapsedTime(running.StartedAt).TotalSeconds);
            }
        }
        finally
        {
            _starting.Release();
        }
    }

    /// <summary>Ends the server, which gives its memory back. The next <see cref="EnsureStartedAsync"/> starts a new one.</summary>
    public void Stop()
    {
        Running? running;

        lock (_sync)
        {
            running = _running;
            _running = null;
        }

        if (running is not null)
        {
            End(running);
            _logger.ServerStopped();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
        Client.Dispose();

        // The start gate is left alone: someone may still be waiting on it, and it holds nothing to release.
    }

    private static int FreePort()
    {
        // Asking for port 0 makes the system choose a free one. It is free again the moment the listener
        // closes; another program taking it before the server does is possible and shows up as a failed start.
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void End(Running running)
    {
        running.Process.Kill();
        running.Process.Dispose();
    }

    private Running GetOrLaunch()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_running is not null)
            {
                if (!_running.Process.HasExited)
                {
                    return _running;
                }

                _logger.ServerExited();
                _running.Process.Dispose();
                _running = null;
            }

            int port = FreePort();
            string apiKey = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            IServerProcess process;

            try
            {
                process = _launcher.Start(new ServerStartInfo(
                    _options.ExecutablePath,
                    Arguments(port),
                    new Dictionary<string, string> { [ApiKeyVariable] = apiKey }));
            }
            catch (Exception exception)
            {
                _logger.ServerStartFailed(exception);
                throw new TranslationProviderException(
                    ProviderFailureKind.Unavailable, "The model server could not be started.", exception);
            }

            _running = new Running(process, port, apiKey, Stopwatch.GetTimestamp());
            return _running;
        }
    }

    private string[] Arguments(int port) =>
    [
        "-m", _options.ModelPath,
        "--host", Loopback,
        "--port", port.ToString(CultureInfo.InvariantCulture),
        "-c", _options.ContextSize.ToString(CultureInfo.InvariantCulture),

        // One request at a time: translation is bound by the processor, and a second request
        // running alongside would only make both slower.
        "-np", "1",
        "-ngl", _options.GpuLayers.ToString(CultureInfo.InvariantCulture),
    ];

    private async Task WaitUntilReadyAsync(Running running, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (running.Process.HasExited)
            {
                throw new TranslationProviderException(
                    ProviderFailureKind.Unavailable, "The model server stopped before it was ready.");
            }

            if (Stopwatch.GetElapsedTime(running.StartedAt) > _options.StartupTimeout)
            {
                throw new TranslationProviderException(
                    ProviderFailureKind.Unavailable, "The model server did not become ready in time.");
            }

            if (await IsHealthyAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(_options.HealthPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    // The server answers its health check with an error while it is loading the model, and refuses
    // the connection before that. Both mean "not yet".
    private async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probe.CancelAfter(_options.HealthProbeTimeout);

        try
        {
            using HttpResponseMessage response = await Client.GetAsync(HealthEndpoint, probe.Token).ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private void Discard(Running running)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_running, running))
            {
                _running = null;
            }
        }

        End(running);
    }

    private Running? Current()
    {
        lock (_sync)
        {
            return _running;
        }
    }

    private sealed class Running(IServerProcess process, int port, string apiKey, long startedAt)
    {
        public IServerProcess Process { get; } = process;

        public int Port { get; } = port;

        public string ApiKey { get; } = apiKey;

        public long StartedAt { get; } = startedAt;

        /// <summary>Set once the server has answered. Read and written only by whoever holds the start gate.</summary>
        public bool Ready { get; set; }
    }

    // Points every request at the server running now and adds its key.
    private sealed class EndpointHandler(LlamaServer owner, HttpMessageHandler transport) : DelegatingHandler(transport)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Running? running = owner.Current();

            if (running is null || request.RequestUri is null)
            {
                throw new HttpRequestException("The model server is not running.");
            }

            request.RequestUri = new UriBuilder(request.RequestUri) { Port = running.Port }.Uri;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", running.ApiKey);

            return base.SendAsync(request, cancellationToken);
        }
    }
}