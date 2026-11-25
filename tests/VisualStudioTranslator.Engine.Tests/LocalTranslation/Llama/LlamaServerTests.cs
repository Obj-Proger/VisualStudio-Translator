using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Engine.LocalTranslation.Llama;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Llama;

public sealed class LlamaServerTests : IDisposable
{
    private const string Executable = @"C:\engine\llama-server.exe";
    private const string Model = @"C:\models\model.gguf";

    private const int AllLayers = 999;

    private readonly FakeLauncher _launcher = new();
    private readonly FakeTransport _transport = new();
    private readonly string _verdictPath =
        Path.Combine(Path.GetTempPath(), "vst-gpu-" + Path.GetRandomFileName(), "gpu-unusable.txt");

    public void Dispose()
    {
        _transport.Dispose();

        string directory = Path.GetDirectoryName(_verdictPath)!;

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private LlamaServer Server(
        TimeSpan? startupTimeout = null,
        int gpuLayers = 0,
        GpuVerdict? verdict = null,
        IDeviceLister? devices = null,
        GpuRanking? ranking = null) => new(
        _launcher,
        new LlamaServerOptions
        {
            ExecutablePath = Executable,
            ModelPath = Model,
            HealthPollInterval = TimeSpan.FromMilliseconds(5),
            StartupTimeout = startupTimeout ?? TimeSpan.FromSeconds(30),
            GpuLayers = gpuLayers,
        },
        _transport,
        NullLogger<LlamaServer>.Instance,
        verdict,
        devices,
        ranking);

    private static readonly GpuDevice Nvidia = new("Vulkan0", "NVIDIA GeForce RTX 4070 Laptop GPU", 7963);
    private static readonly GpuDevice Radeon = new("Vulkan1", "AMD Radeon 780M Graphics", 48899);

    private string RankingPath => Path.Combine(Path.GetDirectoryName(_verdictPath)!, "gpu-ranking.txt");

    // Which card the server on this port was started on, or null for the processor or "any card".
    private string? DeviceAt(int port)
    {
        ServerStartInfo info = _launcher.Started.Last(started => Port(started) == port);
        List<string> arguments = [.. info.Arguments];
        int index = arguments.IndexOf("--device");

        return index < 0 ? null : arguments[index + 1];
    }

    private static string? Device(ServerStartInfo info)
    {
        List<string> arguments = [.. info.Arguments];
        int index = arguments.IndexOf("--device");

        return index < 0 ? null : arguments[index + 1];
    }

    private static int GpuLayers(ServerStartInfo info) => int.Parse(Argument(info, "-ngl"), CultureInfo.InvariantCulture);

    private static string Argument(ServerStartInfo info, string name)
    {
        List<string> arguments = [.. info.Arguments];

        return arguments[arguments.IndexOf(name) + 1];
    }

    private static int Port(ServerStartInfo info) => int.Parse(Argument(info, "--port"), CultureInfo.InvariantCulture);

    [Fact]
    public async Task EnsureStartedAsync_StartsTheServerWithTheModelOnThisMachineOnly()
    {
        using LlamaServer server = Server();

        await server.EnsureStartedAsync(Token);

        ServerStartInfo info = _launcher.Started.Should().ContainSingle().Which;
        info.ExecutablePath.Should().Be(Executable);
        Argument(info, "-m").Should().Be(Model);
        Argument(info, "--host").Should().Be("127.0.0.1");
        Port(info).Should().BeInRange(1, 65535);
    }

    [Fact]
    public async Task EnsureStartedAsync_HandsTheKeyOverThroughTheEnvironmentNotTheCommandLine()
    {
        using LlamaServer server = Server();

        await server.EnsureStartedAsync(Token);

        ServerStartInfo info = _launcher.Started.Single();
        string key = info.Environment["LLAMA_API_KEY"];
        key.Should().MatchRegex("^[0-9a-f]{64}$");
        info.Arguments.Should().NotContain(key);
    }

    [Fact]
    public async Task EnsureStartedAsync_WaitsWhileTheServerIsStillLoadingTheModel()
    {
        _transport.LoadingProbes = 3;
        using LlamaServer server = Server();

        await server.EnsureStartedAsync(Token);

        _transport.HealthProbes.Should().Be(4);
    }

    [Fact]
    public async Task EnsureStartedAsync_WaitsWhileTheServerIsNotListeningYet()
    {
        _transport.RefusedProbes = 3;
        using LlamaServer server = Server();

        await server.EnsureStartedAsync(Token);

        _transport.HealthProbes.Should().Be(4);
    }

    [Fact]
    public async Task EnsureStartedAsync_CalledAgain_UsesTheServerThatIsRunning()
    {
        using LlamaServer server = Server();

        await server.EnsureStartedAsync(Token);
        await server.EnsureStartedAsync(Token);

        _launcher.Started.Should().ContainSingle();
    }

    [Fact]
    public async Task EnsureStartedAsync_CalledByManyAtOnce_StartsOneServer()
    {
        _transport.LoadingProbes = 5;
        using LlamaServer server = Server();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => server.EnsureStartedAsync(Token)));

        _launcher.Started.Should().ContainSingle();
    }

    [Fact]
    public async Task EnsureStartedAsync_ServerThatEndsWhileStarting_ThrowsUnavailable()
    {
        _launcher.ExitAtOnce = true;
        _transport.Healthy = false;
        using LlamaServer server = Server();

        Func<Task> act = () => server.EnsureStartedAsync(Token);

        (await act.Should().ThrowAsync<TranslationProviderException>())
            .Which.Kind.Should().Be(ProviderFailureKind.Unavailable);
    }

    [Fact]
    public async Task EnsureStartedAsync_ServerThatNeverBecomesReady_ThrowsUnavailableAndStopsIt()
    {
        _transport.Healthy = false;
        using LlamaServer server = Server(startupTimeout: TimeSpan.FromMilliseconds(100));

        Func<Task> act = () => server.EnsureStartedAsync(Token);

        (await act.Should().ThrowAsync<TranslationProviderException>())
            .Which.Kind.Should().Be(ProviderFailureKind.Unavailable);
        _launcher.Processes.Should().ContainSingle().Which.Killed.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureStartedAsync_ProgramThatCannotBeStarted_ThrowsUnavailable()
    {
        _launcher.Failure = new InvalidOperationException("No such file.");
        using LlamaServer server = Server();

        Func<Task> act = () => server.EnsureStartedAsync(Token);

        (await act.Should().ThrowAsync<TranslationProviderException>())
            .Which.Kind.Should().Be(ProviderFailureKind.Unavailable);
    }

    [Fact]
    public async Task EnsureStartedAsync_AfterAFailedStart_TriesAgain()
    {
        _launcher.Failure = new InvalidOperationException("No such file.");
        using LlamaServer server = Server();
        Func<Task> failing = () => server.EnsureStartedAsync(Token);
        await failing.Should().ThrowAsync<TranslationProviderException>();

        _launcher.Failure = null;
        await server.EnsureStartedAsync(Token);

        _launcher.Started.Should().ContainSingle();
    }

    [Fact]
    public async Task EnsureStartedAsync_ServerThatDiedSinceItWasReady_StartsANewOne()
    {
        using LlamaServer server = Server();
        await server.EnsureStartedAsync(Token);

        _launcher.Processes.Single().HasExited = true;
        await server.EnsureStartedAsync(Token);

        _launcher.Started.Should().HaveCount(2);
        Port(_launcher.Started[1]).Should().BeInRange(1, 65535);
    }

    [Fact]
    public async Task EnsureStartedAsync_CallerThatGivesUp_LeavesTheServerToFinishStarting()
    {
        _transport.Healthy = false;
        using LlamaServer server = Server();
        using CancellationTokenSource impatient = CancellationTokenSource.CreateLinkedTokenSource(Token);
        impatient.CancelAfter(TimeSpan.FromMilliseconds(50));

        Func<Task> act = () => server.EnsureStartedAsync(impatient.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        _launcher.Processes.Should().ContainSingle().Which.Killed.Should().BeFalse();

        // The next caller finds the same server and waits for it, not for a new one.
        _transport.Healthy = true;
        await server.EnsureStartedAsync(Token);

        _launcher.Started.Should().ContainSingle();
    }

    [Fact]
    public async Task EnsureStartedAsync_ProcessorOnly_StartsWithNoLayersOnTheCardAndAsksNoCheck()
    {
        using LlamaServer server = Server();

        await server.EnsureStartedAsync(Token);

        GpuLayers(_launcher.Started.Single()).Should().Be(0);
        _transport.Checks.Should().Be(0);
    }

    [Fact]
    public async Task EnsureStartedAsync_CardThatAnswersRight_IsUsed()
    {
        using LlamaServer server = Server(gpuLayers: AllLayers);

        await server.EnsureStartedAsync(Token);

        GpuLayers(_launcher.Started.Single()).Should().Be(AllLayers);
        _transport.Checks.Should().Be(1);
    }

    [Fact]
    public async Task EnsureStartedAsync_CardThatAnswersNonsense_IsReplacedByTheProcessor()
    {
        _transport.CheckAnswer = "<unused42><unused42><unused42>";
        using LlamaServer server = Server(gpuLayers: AllLayers);

        await server.EnsureStartedAsync(Token);

        _launcher.Started.Select(GpuLayers).Should().Equal(AllLayers, 0);
        _launcher.Processes[0].Killed.Should().BeTrue();
        _launcher.Processes[1].Killed.Should().BeFalse();
    }

    [Fact]
    public async Task EnsureStartedAsync_CardThatCrashesTheServer_IsReplacedByTheProcessor()
    {
        _launcher.ExitingStarts = 1;
        using LlamaServer server = Server(gpuLayers: AllLayers);

        await server.EnsureStartedAsync(Token);

        _launcher.Started.Select(GpuLayers).Should().Equal(AllLayers, 0);
    }

    [Fact]
    public async Task EnsureStartedAsync_CheckFailingWithAnError_IsReplacedByTheProcessor()
    {
        _transport.CheckAnswer = null;
        using LlamaServer server = Server(gpuLayers: AllLayers);

        await server.EnsureStartedAsync(Token);

        _launcher.Started.Select(GpuLayers).Should().Equal(AllLayers, 0);
    }

    [Fact]
    public async Task EnsureStartedAsync_AfterTheCardFailed_LaterStartsGoStraightToTheProcessor()
    {
        _transport.CheckAnswer = "nonsense";
        using LlamaServer server = Server(gpuLayers: AllLayers);
        await server.EnsureStartedAsync(Token);

        server.Stop();
        await server.EnsureStartedAsync(Token);

        _launcher.Started.Select(GpuLayers).Should().Equal(AllLayers, 0, 0);
        _transport.Checks.Should().Be(1);
    }

    [Fact]
    public async Task EnsureStartedAsync_CardFailure_IsRememberedForTheNextRunOfTheEngine()
    {
        _transport.CheckAnswer = "nonsense";

        using (LlamaServer first = Server(gpuLayers: AllLayers, verdict: new GpuVerdict(_verdictPath, TimeProvider.System)))
        {
            await first.EnsureStartedAsync(Token);
        }

        using LlamaServer second = Server(gpuLayers: AllLayers, verdict: new GpuVerdict(_verdictPath, TimeProvider.System));
        await second.EnsureStartedAsync(Token);

        _launcher.Started.Select(GpuLayers).Should().Equal(AllLayers, 0, 0);
    }

    [Fact]
    public async Task EnsureStartedAsync_CardThatFailedWithAnotherBuild_GetsANewChance()
    {
        GpuVerdict verdict = new(_verdictPath, TimeProvider.System);
        verdict.MarkUnusable(@"C:\engine\older\llama-server.exe");
        using LlamaServer server = Server(gpuLayers: AllLayers, verdict: verdict);

        await server.EnsureStartedAsync(Token);

        GpuLayers(_launcher.Started.Single()).Should().Be(AllLayers);
    }

    [Fact]
    public async Task EnsureStartedAsync_ProcessorFailingToo_ThrowsUnavailable()
    {
        _transport.CheckAnswer = "nonsense";
        _launcher.ExitingStarts = 2;
        using LlamaServer server = Server(gpuLayers: AllLayers);

        Func<Task> act = () => server.EnsureStartedAsync(Token);

        (await act.Should().ThrowAsync<TranslationProviderException>()).Which.Kind.Should().Be(ProviderFailureKind.Unavailable);
        _launcher.Started.Select(GpuLayers).Should().Equal(AllLayers, 0);
    }

    [Fact]
    public async Task EnsureStartedAsync_CardThatWorksAgain_ForgetsEarlierFailures()
    {
        FakeTimeProvider time = new();
        GpuVerdict verdict = new(_verdictPath, time);
        verdict.MarkUnusable(Executable);
        verdict.MarkUnusable(Executable);
        time.Advance(TimeSpan.FromDays(2));
        using LlamaServer server = Server(gpuLayers: AllLayers, verdict: verdict);

        await server.EnsureStartedAsync(Token);

        GpuLayers(_launcher.Started.Single()).Should().Be(AllLayers);
        File.Exists(_verdictPath).Should().BeFalse();
    }

    [Fact]
    public async Task EnsureStartedAsync_OneCard_GoesToItWithoutTiming()
    {
        using LlamaServer server = Server(gpuLayers: AllLayers, devices: new FakeDevices(Radeon));

        await server.EnsureStartedAsync(Token);

        Device(_launcher.Started.Single()).Should().Be("Vulkan1");
        _transport.Timings.Should().Be(0);
    }

    [Fact]
    public async Task EnsureStartedAsync_NoCards_GoesToTheProcessor()
    {
        using LlamaServer server = Server(gpuLayers: AllLayers, devices: new FakeDevices());

        await server.EnsureStartedAsync(Token);

        ServerStartInfo info = _launcher.Started.Single();
        Device(info).Should().Be("none");
        GpuLayers(info).Should().Be(0);
    }

    [Fact]
    public async Task EnsureStartedAsync_CardsThatCannotBeListed_LeavesTheChoiceToTheServer()
    {
        using LlamaServer server = Server(gpuLayers: AllLayers, devices: new FakeDevices(null));

        await server.EnsureStartedAsync(Token);

        ServerStartInfo info = _launcher.Started.Single();
        Device(info).Should().BeNull();
        GpuLayers(info).Should().Be(AllLayers);
    }

    [Fact]
    public async Task EnsureStartedAsync_SeveralCards_TimesEachAndGoesToTheFastest()
    {
        _transport.TimingDelayAt = port => TimeSpan.FromMilliseconds(DeviceAt(port) == "Vulkan0" ? 10 : 300);
        using LlamaServer server = Server(gpuLayers: AllLayers, devices: new FakeDevices(Radeon, Nvidia));

        await server.EnsureStartedAsync(Token);

        _launcher.Started.Select(Device).Should().Equal("Vulkan1", "Vulkan0", "Vulkan0");
        _launcher.Processes.Take(2).Should().OnlyContain(process => process.Killed);
        _launcher.Processes[2].Killed.Should().BeFalse();
    }

    [Fact]
    public async Task EnsureStartedAsync_CardsTimedBefore_GoesToTheFastestWithoutTimingAgain()
    {
        GpuRanking ranking = new(RankingPath);
        ranking.Save(Executable, [Nvidia, Radeon]);
        using LlamaServer server = Server(gpuLayers: AllLayers, devices: new FakeDevices(Radeon, Nvidia), ranking: ranking);

        await server.EnsureStartedAsync(Token);

        Device(_launcher.Started.Single()).Should().Be("Vulkan0");
        _transport.Timings.Should().Be(0);
    }

    [Fact]
    public async Task EnsureStartedAsync_TimingIsRememberedForTheNextRun()
    {
        _transport.TimingDelayAt = port => TimeSpan.FromMilliseconds(DeviceAt(port) == "Vulkan0" ? 10 : 300);

        using (LlamaServer first = Server(gpuLayers: AllLayers, devices: new FakeDevices(Radeon, Nvidia), ranking: new(RankingPath)))
        {
            await first.EnsureStartedAsync(Token);
        }

        using LlamaServer second = Server(gpuLayers: AllLayers, devices: new FakeDevices(Radeon, Nvidia), ranking: new(RankingPath));
        await second.EnsureStartedAsync(Token);

        _transport.Timings.Should().Be(2);
        Device(_launcher.Started[^1]).Should().Be("Vulkan0");
    }

    [Fact]
    public async Task EnsureStartedAsync_FastestCardFailing_GoesToTheNextThenTheProcessor()
    {
        GpuRanking ranking = new(RankingPath);
        ranking.Save(Executable, [Nvidia, Radeon]);
        _transport.CheckAnswerAt = port => DeviceAt(port) is "Vulkan0" or "Vulkan1" ? "nonsense" : "OK";
        using LlamaServer server = Server(gpuLayers: AllLayers, devices: new FakeDevices(Radeon, Nvidia), ranking: ranking);

        await server.EnsureStartedAsync(Token);

        _launcher.Started.Select(Device).Should().Equal("Vulkan0", "Vulkan1", "none");
    }

    [Fact]
    public async Task EnsureStartedAsync_CardThatFailedEarlier_IsPassedOver()
    {
        GpuVerdict verdict = new(_verdictPath, TimeProvider.System);
        verdict.MarkUnusable(Executable, Nvidia.Name);
        GpuRanking ranking = new(RankingPath);
        ranking.Save(Executable, [Nvidia, Radeon]);
        using LlamaServer server = Server(
            gpuLayers: AllLayers, verdict: verdict, devices: new FakeDevices(Radeon, Nvidia), ranking: ranking);

        await server.EnsureStartedAsync(Token);

        Device(_launcher.Started.Single()).Should().Be("Vulkan1");
    }

    [Fact]
    public async Task EnsureStartedAsync_CardFailingWhileTimed_GoesLastAndIsRemembered()
    {
        _transport.CheckAnswerAt = port => DeviceAt(port) == "Vulkan0" ? "nonsense" : "OK";
        GpuVerdict verdict = new(_verdictPath, TimeProvider.System);
        using LlamaServer server = Server(
            gpuLayers: AllLayers, verdict: verdict, devices: new FakeDevices(Nvidia, Radeon), ranking: new(RankingPath));

        await server.EnsureStartedAsync(Token);

        Device(_launcher.Started[^1]).Should().Be("Vulkan1");
        verdict.IsUnusable(Executable, Nvidia.Name).Should().BeTrue();
        verdict.IsUnusable(Executable, Radeon.Name).Should().BeFalse();
    }

    [Fact]
    public async Task Client_SendsRequestsToTheRunningServersPortWithItsKey()
    {
        using LlamaServer server = Server();
        await server.EnsureStartedAsync(Token);
        ServerStartInfo info = _launcher.Started.Single();

        using HttpResponseMessage response = await server.Client.GetAsync(new Uri("v1/models", UriKind.Relative), Token);

        (Uri address, string? authorization) = _transport.Requests[^1];
        address.Should().Be(new Uri($"http://127.0.0.1:{Argument(info, "--port")}/v1/models"));
        authorization.Should().Be("Bearer " + info.Environment["LLAMA_API_KEY"]);
    }

    [Fact]
    public async Task Client_NoServerRunning_FailsLikeANetworkErrorWithoutSendingAnything()
    {
        using LlamaServer server = Server();

        Func<Task> act = () => server.Client.GetAsync(new Uri("v1/models", UriKind.Relative), Token);

        await act.Should().ThrowAsync<HttpRequestException>();
        _transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Stop_EndsTheServer_AndTheNextUseStartsItAgain()
    {
        using LlamaServer server = Server();
        await server.EnsureStartedAsync(Token);

        server.Stop();

        FakeProcess stopped = _launcher.Processes.Single();
        stopped.Killed.Should().BeTrue();
        stopped.Disposed.Should().BeTrue();

        await server.EnsureStartedAsync(Token);
        _launcher.Started.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReconfigureAsync_AnotherModel_StopsTheServerAndTheNextStartLoadsIt()
    {
        using LlamaServer server = Server();
        await server.EnsureStartedAsync(Token);

        await server.ReconfigureAsync(@"C:\models\light.gguf", 0, Token);

        _launcher.Processes.Single().Killed.Should().BeTrue();
        server.ModelPath.Should().Be(@"C:\models\light.gguf");

        await server.EnsureStartedAsync(Token);
        Argument(_launcher.Started.Last(), "-m").Should().Be(@"C:\models\light.gguf");
    }

    [Fact]
    public async Task ReconfigureAsync_CardTurnedOff_GoesToTheProcessorWithoutListingTheCards()
    {
        FakeDevices devices = new(Nvidia, Radeon);
        using LlamaServer server = Server(gpuLayers: AllLayers, devices: devices, ranking: new GpuRanking(RankingPath));
        await server.EnsureStartedAsync(Token);
        int listed = devices.Calls;

        await server.ReconfigureAsync(Model, 0, Token);
        await server.EnsureStartedAsync(Token);

        Device(_launcher.Started.Last()).Should().Be("none");
        GpuLayers(_launcher.Started.Last()).Should().Be(0);
        devices.Calls.Should().Be(listed);
    }

    [Fact]
    public async Task ReconfigureAsync_CardTurnedBackOn_GoesToTheFastestCardWithoutTimingAgain()
    {
        _transport.TimingDelayAt = port => TimeSpan.FromMilliseconds(DeviceAt(port) == "Vulkan0" ? 10 : 300);
        FakeDevices devices = new(Radeon, Nvidia);
        using LlamaServer server = Server(gpuLayers: AllLayers, devices: devices, ranking: new GpuRanking(RankingPath));
        await server.EnsureStartedAsync(Token);
        await server.ReconfigureAsync(Model, 0, Token);
        await server.EnsureStartedAsync(Token);
        int timings = _transport.Timings;

        await server.ReconfigureAsync(Model, AllLayers, Token);
        await server.EnsureStartedAsync(Token);

        Device(_launcher.Started.Last()).Should().Be(Nvidia.Id);
        _transport.Timings.Should().Be(timings);
    }

    [Fact]
    public async Task ReconfigureAsync_NothingChanged_LeavesTheServerRunning()
    {
        using LlamaServer server = Server();
        await server.EnsureStartedAsync(Token);

        await server.ReconfigureAsync(Model, 0, Token);
        await server.EnsureStartedAsync(Token);

        _launcher.Processes.Single().Killed.Should().BeFalse();
        _launcher.Started.Should().ContainSingle();
    }

    [Fact]
    public void Stop_NothingRunning_DoesNothing()
    {
        using LlamaServer server = Server();

        Action act = server.Stop;

        act.Should().NotThrow();
        _launcher.Started.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispose_EndsTheServerAndRefusesToStartAnother()
    {
        LlamaServer server = Server();
        await server.EnsureStartedAsync(Token);

        server.Dispose();

        _launcher.Processes.Single().Killed.Should().BeTrue();
        Func<Task> act = () => server.EnsureStartedAsync(Token);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    private sealed class FakeProcess : IServerProcess
    {
        private volatile bool _hasExited;

        public bool HasExited
        {
            get => _hasExited;
            set => _hasExited = value;
        }

        public bool Killed { get; private set; }

        public bool Disposed { get; private set; }

        public void Kill()
        {
            Killed = true;
            _hasExited = true;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeLauncher : IServerProcessLauncher
    {
        public List<ServerStartInfo> Started { get; } = [];

        public List<FakeProcess> Processes { get; } = [];

        public Exception? Failure { get; set; }

        /// <summary>Whether a started program has already ended by the time anyone looks.</summary>
        public bool ExitAtOnce { get; set; }

        /// <summary>How many of the first programs started end at once, as one would that a driver crashes.</summary>
        public int ExitingStarts { get; set; }

        public IServerProcess Start(ServerStartInfo info)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            FakeProcess process = new() { HasExited = ExitAtOnce || Started.Count < ExitingStarts };
            Started.Add(info);
            Processes.Add(process);

            return process;
        }
    }

    // Stands in for the network and the server behind it. Remembers every request that reached it.
    private sealed class FakeTransport : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly List<(Uri Address, string? Authorization)> _requests = [];
        private volatile bool _healthy = true;
        private int _healthProbes;
        private int _checks;
        private int _timings;

        /// <summary>Whether the health check succeeds, once the probes below are used up.</summary>
        public bool Healthy
        {
            get => _healthy;
            set => _healthy = value;
        }

        /// <summary>How many health checks are answered "still loading" first.</summary>
        public int LoadingProbes { get; set; }

        /// <summary>How many health checks find nobody listening first.</summary>
        public int RefusedProbes { get; set; }

        public int HealthProbes => Volatile.Read(ref _healthProbes);

        /// <summary>What the model answers the check a server on the graphics card is given; null fails the request.</summary>
        public string? CheckAnswer { get; set; } = "OK";

        public int Checks => Volatile.Read(ref _checks);

        public int Timings => Volatile.Read(ref _timings);

        /// <summary>The check's answer from the server on this port, in place of <see cref="CheckAnswer"/>.</summary>
        public Func<int, string?>? CheckAnswerAt { get; set; }

        /// <summary>How long the server on this port takes over the answer cards are timed on.</summary>
        public Func<int, TimeSpan>? TimingDelayAt { get; set; }

        public IReadOnlyList<(Uri Address, string? Authorization)> Requests
        {
            get
            {
                lock (_gate)
                {
                    return [.. _requests];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                _requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString()));
            }

            int port = request.RequestUri!.Port;

            if (request.RequestUri!.AbsolutePath == "/v1/chat/completions")
            {
                string question = await request.Content!.ReadAsStringAsync(cancellationToken);

                if (question.Contains("Count from one", StringComparison.Ordinal))
                {
                    await Task.Delay(TimingDelayAt?.Invoke(port) ?? TimeSpan.Zero, cancellationToken);
                    Interlocked.Increment(ref _timings);

                    return Json(new JsonObject { ["usage"] = new JsonObject { ["completion_tokens"] = 20 } });
                }

                Interlocked.Increment(ref _checks);
                string? answer = CheckAnswerAt is null ? CheckAnswer : CheckAnswerAt(port);

                if (answer is null)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                JsonObject choice = new() { ["message"] = new JsonObject { ["content"] = answer } };
                return Json(new JsonObject { ["choices"] = new JsonArray(choice) });
            }

            if (request.RequestUri!.AbsolutePath != "/health")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            int probe = Interlocked.Increment(ref _healthProbes);

            if (probe <= RefusedProbes)
            {
                throw new HttpRequestException("Connection refused.");
            }

            bool ready = Healthy && probe > LoadingProbes;

            return new HttpResponseMessage(ready ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);
        }

        private static HttpResponseMessage Json(JsonObject body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    private sealed class FakeDevices(params GpuDevice[]? devices) : IDeviceLister
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<IReadOnlyList<GpuDevice>?> ListAsync(string serverPath, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<GpuDevice>?>(devices);
        }
    }
}