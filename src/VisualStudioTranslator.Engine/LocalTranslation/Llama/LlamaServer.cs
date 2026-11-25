using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
/// <para>
/// With <see cref="LlamaServerOptions.GpuLayers"/> above zero the model goes to a graphics card, which is several
/// times faster when it works. Where it goes is a list of places to try, best first, ending with the processor:
/// <list type="number">
/// <item>The machine's cards are listed (<see cref="IDeviceLister"/>). With more than one, each is tried once
/// and timed on a short answer, and the order is remembered (<see cref="GpuRanking"/>): the memory a card reports
/// cannot tell, since a card built into the processor reports a share of the machine's own.</item>
/// <item>The model goes to one card only, the fastest. Split across a fast card and a slow one it would run at
/// the pace of the slow one, plus the copying between them.</item>
/// <item>A card can fail: its driver can crash the server while it loads the model, or let it run and compute
/// nonsense. So a server on a card is asked one question with a known answer before it is used, and if it fails
/// either way the next place on the list is tried, down to the processor. The rest of this run keeps away from
/// the card, and later runs for a while (see <see cref="GpuVerdict"/>).</item>
/// </list>
/// When the cards cannot be listed, the server is left to pick, as it would on its own.
/// </para>
/// </summary>
internal sealed class LlamaServer : IDisposable
{
    private const string Loopback = "127.0.0.1";
    private const string ApiKeyVariable = "LLAMA_API_KEY";

    private static readonly Uri HealthEndpoint = new("health", UriKind.Relative);
    private static readonly Uri ChatEndpoint = new("v1/chat/completions", UriKind.Relative);

    // A question any working model answers the same way, in a few tokens.
    private static readonly string CheckRequest = Ask("Reply with exactly the word OK and nothing else.", 8);

    // A longer answer, also the same every time, to time a card by.
    private static readonly string TimingRequest = Ask("Count from one to twenty in English words, separated by commas.", 64);

    private readonly IServerProcessLauncher _launcher;
    private readonly LlamaServerOptions _options;
    private readonly GpuVerdict? _gpuVerdict;
    private readonly IDeviceLister? _devices;
    private readonly GpuRanking? _ranking;
    private readonly ILogger<LlamaServer> _logger;

    // Held by whoever is starting the server, so that callers arriving together start one, not several.
    private readonly SemaphoreSlim _starting = new(1, 1);

    // Guards the fields below. Never held across an await, so stopping never waits for a start.
    private readonly Lock _sync = new();
    private Running? _running;
    private bool _disposed;

    // Where the server may run, best first, worked out at the first start; and which of them the next start uses.
    // A place that fails is passed over for the rest of the run. Only touched by whoever holds the start gate.
    private List<ComputePlace>? _places;
    private int _placeIndex;

    /// <param name="transport">
    /// What carries the requests: the real network in the Engine, a stand-in in tests. Disposed with this object.
    /// </param>
    /// <param name="gpuVerdict">
    /// Where a failure of a graphics card is remembered between runs; without one, only for this run.
    /// </param>
    /// <param name="devices">How the machine's graphics cards are found; without it, the server picks.</param>
    /// <param name="ranking">Where the order of the cards is remembered between runs; without it, they are timed each run.</param>
    public LlamaServer(
        IServerProcessLauncher launcher,
        LlamaServerOptions options,
        HttpMessageHandler transport,
        ILogger<LlamaServer> logger,
        GpuVerdict? gpuVerdict = null,
        IDeviceLister? devices = null,
        GpuRanking? ranking = null)
    {
        _launcher = launcher;
        _options = options;
        _logger = logger;
        _gpuVerdict = gpuVerdict;
        _devices = devices;
        _ranking = ranking;

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
            if (Current() is { Ready: true } ready && !ready.Process.HasExited)
            {
                return;
            }

            _places ??= await PlanAsync(cancellationToken).ConfigureAwait(false);

            Running running = GetOrLaunch(_places[_placeIndex]);

            while (!running.Ready)
            {
                try
                {
                    await WaitUntilReadyAsync(running, cancellationToken).ConfigureAwait(false);

                    if (running.Place.UsesGpu)
                    {
                        await CheckAnswersAsync(cancellationToken).ConfigureAwait(false);
                        _gpuVerdict?.MarkUsable(_options.ExecutablePath, running.Place.Device);
                    }

                    running.Ready = true;
                }
                catch (TranslationProviderException exception) when (running.Place.UsesGpu)
                {
                    // The processor ends the list, so there is always a next place to go to.
                    GiveUp(running, exception);
                    _placeIndex++;
                    running = GetOrLaunch(_places[_placeIndex]);
                }
                catch (TranslationProviderException)
                {
                    _logger.ServerNotReady();
                    Discard(running);
                    throw;
                }
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.ServerReady(Stopwatch.GetElapsedTime(running.StartedAt).TotalSeconds, running.Place.Description);
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

    private static string Ask(string question, int maxTokens) => JsonSerializer.Serialize(new
    {
        messages = new[] { new { role = "user", content = question } },
        max_tokens = maxTokens,
        temperature = 0,
        chat_template_kwargs = new { enable_thinking = false },
    });

    // Where the server may run this run, best first, the processor last.
    private async Task<List<ComputePlace>> PlanAsync(CancellationToken cancellationToken)
    {
        string server = _options.ExecutablePath;

        if (_options.GpuLayers <= 0)
        {
            return [ComputePlace.Processor];
        }

        IReadOnlyList<GpuDevice>? devices = _devices is null
            ? null
            : await _devices.ListAsync(server, cancellationToken).ConfigureAwait(false);

        if (devices is null)
        {
            // Not known which cards there are: the server picks, as it does when left to itself.
            return _gpuVerdict?.IsUnusable(server) == true
                ? [ComputePlace.Processor]
                : [ComputePlace.AnyCard(_options.GpuLayers), ComputePlace.Processor];
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.CardsFound(devices.Count, string.Join(", ", devices.Select(device => device.Name)));
        }

        IReadOnlyList<GpuDevice> ranked = devices.Count <= 1
            ? devices
            : _ranking?.Get(server, devices) ?? await RankAsync(devices, cancellationToken).ConfigureAwait(false);

        return
        [
            .. ranked
                .Where(device => _gpuVerdict?.IsUnusable(server, device.Name) != true)
                .Select(device => ComputePlace.Card(device, _options.GpuLayers)),
            ComputePlace.Processor,
        ];
    }

    // Tries each card in turn and times it on the same short answer. A card that fails is remembered as such and
    // goes last; the others go fastest first.
    private async Task<IReadOnlyList<GpuDevice>> RankAsync(IReadOnlyList<GpuDevice> devices, CancellationToken cancellationToken)
    {
        Dictionary<GpuDevice, double> speeds = [];

        foreach (GpuDevice device in devices)
        {
            Running running = GetOrLaunch(ComputePlace.Card(device, _options.GpuLayers));

            try
            {
                await WaitUntilReadyAsync(running, cancellationToken).ConfigureAwait(false);
                await CheckAnswersAsync(cancellationToken).ConfigureAwait(false);
                speeds[device] = await TimeAnswerAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (TranslationProviderException exception)
            {
                GiveUp(running, exception);
                continue;
            }
            catch (OperationCanceledException)
            {
                // A half-timed card must not be left running as if it were the chosen one.
                Discard(running);
                throw;
            }

            Discard(running);
        }

        List<GpuDevice> ranked = [.. devices.OrderByDescending(device => speeds.GetValueOrDefault(device, -1))];

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.CardsRanked(string.Join(", ", ranked.Select(device =>
                speeds.TryGetValue(device, out double speed)
                    ? $"{device.Name} ({speed.ToString("F1", CultureInfo.InvariantCulture)} tokens/s)"
                    : $"{device.Name} (failed)")));
        }

        _ranking?.Save(_options.ExecutablePath, ranked);
        return ranked;
    }

    // Tokens of answer a second, waiting included: what the user would feel.
    private async Task<double> TimeAnswerAsync(CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        string text = await AskAsync(TimingRequest, cancellationToken).ConfigureAwait(false);
        double seconds = Math.Max(Stopwatch.GetElapsedTime(started).TotalSeconds, 0.001);

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            int tokens = document.RootElement.GetProperty("usage").GetProperty("completion_tokens").GetInt32();

            return tokens / seconds;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // An answer without a count still came back; how fast is then a guess from its length alone.
            return text.Length / seconds;
        }
    }

    private void GiveUp(Running running, TranslationProviderException exception)
    {
        _logger.GpuUnusable(running.Place.Description, exception);
        Discard(running);
        _gpuVerdict?.MarkUnusable(_options.ExecutablePath, running.Place.Device);
    }

    private async Task<string> AskAsync(string request, CancellationToken cancellationToken)
    {
        try
        {
            using StringContent body = new(request, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await Client.PostAsync(ChatEndpoint, body, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new TranslationProviderException(
                ProviderFailureKind.InvalidResponse, "The model server did not answer its check.", exception);
        }
    }

    // Asks the freshly started server a question with a known answer. A card that loads the model but computes
    // nonsense passes the health check and fails here, before any translation is spoilt by it.
    private async Task CheckAnswersAsync(CancellationToken cancellationToken)
    {
        string text = await AskAsync(CheckRequest, cancellationToken).ConfigureAwait(false);
        string answer;

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            answer = document.RootElement
                .GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException
            or IndexOutOfRangeException or InvalidOperationException)
        {
            throw new TranslationProviderException(
                ProviderFailureKind.InvalidResponse, "The model server did not answer its check.", exception);
        }

        if (!answer.Trim().StartsWith("OK", StringComparison.OrdinalIgnoreCase))
        {
            throw new TranslationProviderException(
                ProviderFailureKind.InvalidResponse, "The model server answered its check with something else.");
        }
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

    private Running GetOrLaunch(ComputePlace place)
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
                    Arguments(port, place),
                    new Dictionary<string, string> { [ApiKeyVariable] = apiKey }));
            }
            catch (Exception exception)
            {
                _logger.ServerStartFailed(exception);
                throw new TranslationProviderException(
                    ProviderFailureKind.Unavailable, "The model server could not be started.", exception);
            }

            _running = new Running(process, port, apiKey, Stopwatch.GetTimestamp(), place);
            return _running;
        }
    }

    private string[] Arguments(int port, ComputePlace place) =>
    [
        "-m", _options.ModelPath,
        "--host", Loopback,
        "--port", port.ToString(CultureInfo.InvariantCulture),
        "-c", _options.ContextSize.ToString(CultureInfo.InvariantCulture),

        // One request at a time: translation is bound by the processor, and a second request
        // running alongside would only make both slower.
        "-np", "1",
        .. place.DeviceArgument is { } device ? new[] { "--device", device } : [],
        "-ngl", place.GpuLayers.ToString(CultureInfo.InvariantCulture),
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

    private sealed class Running(IServerProcess process, int port, string apiKey, long startedAt, ComputePlace place)
    {
        public IServerProcess Process { get; } = process;

        public int Port { get; } = port;

        public string ApiKey { get; } = apiKey;

        public long StartedAt { get; } = startedAt;

        public ComputePlace Place { get; } = place;

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