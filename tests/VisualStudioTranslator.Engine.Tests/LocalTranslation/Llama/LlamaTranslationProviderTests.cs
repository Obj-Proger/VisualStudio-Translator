using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Engine.LocalTranslation.Llama;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Llama;

// The provider with the real server manager and translator around it; only the program and the
// network behind them are stand-ins.
public sealed class LlamaTranslationProviderTests : IDisposable
{
    private static readonly LanguagePair EnglishToRussian = LanguagePair.Create("en", "ru")!;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vst-llama-provider-" + Path.GetRandomFileName());
    private readonly LlamaServerOptions _options;
    private readonly FakeLauncher _launcher = new();
    private readonly FakeServer _network = new();
    private readonly FakeTimeProvider _time = new();
    private readonly LlamaServer _server;
    private readonly LlamaTranslationProvider _provider;

    public LlamaTranslationProviderTests()
    {
        Directory.CreateDirectory(_directory);

        _options = new LlamaServerOptions
        {
            ExecutablePath = Path.Combine(_directory, "llama-server.exe"),
            ModelPath = Path.Combine(_directory, "gemma-4-E4B-it.Q4_K_M.gguf"),
            HealthPollInterval = TimeSpan.FromMilliseconds(5),
            IdleTimeout = IdleTimeout,
        };

        File.WriteAllText(_options.ExecutablePath, "server");
        File.WriteAllText(_options.ModelPath, "model");

        _server = new LlamaServer(_launcher, _options, _network, NullLogger<LlamaServer>.Instance);
        _provider = new LlamaTranslationProvider(_server, _options, _time, NullLogger<LlamaTranslationProvider>.Instance);
    }

    public void Dispose()
    {
        _provider.Dispose();

        // Disposes the fake network with it.
        _server.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Task<IReadOnlyList<string>> TranslateAsync(params string[] texts) =>
        _provider.TranslateAsync(EnglishToRussian, texts, Token);

    [Fact]
    public void Info_IsTheLocalProvider()
    {
        ProviderInfo info = _provider.Info;

        info.Id.Should().Be("local");
        info.Kind.Should().Be(ProviderKind.Local);
        info.QualityTier.Should().Be(QualityTiers.Compact);
    }

    [Fact]
    public void Info_RevisionNamesTheModelAndTheInstruction()
    {
        _provider.Info.Revision.Should().Contain("gemma-4-E4B-it.Q4_K_M.gguf")
            .And.Contain("prompt-" + TranslationPrompt.Version);
    }

    [Fact]
    public void Info_RevisionChangesWhenTheModelFileIsReplaced()
    {
        string before = _provider.Info.Revision;

        File.WriteAllText(_options.ModelPath, "another build of the model");

        _provider.Info.Revision.Should().NotBe(before);
    }

    [Fact]
    public void Supports_ServerAndModelInstalled_IsTrue()
    {
        _provider.Supports(EnglishToRussian).Should().BeTrue();
    }

    [Fact]
    public void Supports_ModelMissing_IsFalse()
    {
        File.Delete(_options.ModelPath);

        _provider.Supports(EnglishToRussian).Should().BeFalse();
    }

    [Fact]
    public void Supports_ServerMissing_IsFalse()
    {
        File.Delete(_options.ExecutablePath);

        _provider.Supports(EnglishToRussian).Should().BeFalse();
    }

    [Fact]
    public async Task TranslateAsync_NoTexts_ReturnsNothingAndStartsNoServer()
    {
        IReadOnlyList<string> results = await TranslateAsync();

        results.Should().BeEmpty();
        _launcher.Started.Should().Be(0);
    }

    [Fact]
    public async Task TranslateAsync_TranslatesEveryTextInOrder()
    {
        IReadOnlyList<string> results = await TranslateAsync("Gets the name.", "Sets the name.", "Resets ⟦0⟧.");

        results.Should().Equal("ru: Gets the name.", "ru: Sets the name.", "ru: Resets ⟦0⟧.");
    }

    [Fact]
    public async Task TranslateAsync_StartsOneServerForSeveralRequests()
    {
        await TranslateAsync("Gets the name.");
        await TranslateAsync("Sets the name.");

        _launcher.Started.Should().Be(1);
    }

    [Fact]
    public async Task TranslateAsync_ModelMissing_FailsAsUnsupportedWithoutStartingTheServer()
    {
        File.Delete(_options.ModelPath);

        Func<Task> act = () => TranslateAsync("Gets the name.");

        (await act.Should().ThrowAsync<TranslationProviderException>())
            .Which.Kind.Should().Be(ProviderFailureKind.UnsupportedLanguagePair);
        _launcher.Started.Should().Be(0);
    }

    [Fact]
    public async Task TranslateAsync_ServerFails_PassesTheFailureOn()
    {
        _network.Status = HttpStatusCode.InternalServerError;

        Func<Task> act = () => TranslateAsync("Gets the name.");

        (await act.Should().ThrowAsync<TranslationProviderException>())
            .Which.Kind.Should().Be(ProviderFailureKind.Unavailable);
    }

    [Fact]
    public async Task TranslateAsync_AlreadyCancelled_ThrowsWithoutStartingTheServer()
    {
        CancellationToken cancelled = new(canceled: true);

        Func<Task> act = () => _provider.TranslateAsync(EnglishToRussian, ["Gets the name."], cancelled);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _launcher.Started.Should().Be(0);
    }

    [Fact]
    public async Task TranslateAsync_RequestsArrivingTogether_ReachTheModelOneAtATime()
    {
        _network.Delay = TimeSpan.FromMilliseconds(20);

        await Task.WhenAll(
            TranslateAsync("One.", "Two."),
            TranslateAsync("Three.", "Four."),
            TranslateAsync("Five.", "Six."));

        _network.Translated.Should().HaveCount(6);
        _network.MostAtOnce.Should().Be(1);
    }

    [Fact]
    public async Task Idle_ServerIsStoppedOnceTheTimeoutHasPassedWithoutARequest()
    {
        await TranslateAsync("Gets the name.");

        _time.Advance(IdleTimeout - TimeSpan.FromSeconds(1));
        _launcher.Processes.Single().Killed.Should().BeFalse();

        _time.Advance(TimeSpan.FromSeconds(1));
        _launcher.Processes.Single().Killed.Should().BeTrue();
    }

    [Fact]
    public async Task Idle_ARequestStartsTheWaitOver()
    {
        await TranslateAsync("Gets the name.");
        _time.Advance(IdleTimeout - TimeSpan.FromSeconds(1));

        await TranslateAsync("Sets the name.");
        _time.Advance(TimeSpan.FromSeconds(2));
        _launcher.Processes.Single().Killed.Should().BeFalse();

        _time.Advance(IdleTimeout);
        _launcher.Processes.Single().Killed.Should().BeTrue();
    }

    [Fact]
    public async Task Idle_IsCountedFromTheEndOfARequestNotItsStart()
    {
        await TranslateAsync("Gets the name.");

        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _network.Arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _network.Hold = release.Task;
        Task<IReadOnlyList<string>> slow = TranslateAsync("Sets the name.");
        await _network.Arrived.Task.WaitAsync(Token);

        // The model is busy for longer than the timeout; the server must not be stopped under it.
        _time.Advance(IdleTimeout * 2);
        _launcher.Processes.Single().Killed.Should().BeFalse();

        release.SetResult();
        await slow;

        _time.Advance(IdleTimeout - TimeSpan.FromSeconds(1));
        _launcher.Processes.Single().Killed.Should().BeFalse();

        _time.Advance(TimeSpan.FromSeconds(1));
        _launcher.Processes.Single().Killed.Should().BeTrue();
    }

    [Fact]
    public async Task Idle_AFailedRequestAlsoLeadsToTheServerBeingStopped()
    {
        _network.Status = HttpStatusCode.InternalServerError;
        Func<Task> act = () => TranslateAsync("Gets the name.");
        await act.Should().ThrowAsync<TranslationProviderException>();

        _time.Advance(IdleTimeout);

        _launcher.Processes.Single().Killed.Should().BeTrue();
    }

    [Fact]
    public async Task Idle_NextRequestStartsTheServerAgain()
    {
        await TranslateAsync("Gets the name.");
        _time.Advance(IdleTimeout);

        IReadOnlyList<string> results = await TranslateAsync("Sets the name.");

        results.Should().Equal("ru: Sets the name.");
        _launcher.Started.Should().Be(2);
    }

    private sealed class FakeProcess : IServerProcess
    {
        private volatile bool _killed;

        public bool HasExited => _killed;

        public bool Killed => _killed;

        public void Kill() => _killed = true;

        public void Dispose()
        {
        }
    }

    private sealed class FakeLauncher : IServerProcessLauncher
    {
        private readonly Lock _gate = new();
        private readonly List<FakeProcess> _processes = [];

        public IReadOnlyList<FakeProcess> Processes
        {
            get
            {
                lock (_gate)
                {
                    return [.. _processes];
                }
            }
        }

        public int Started => Processes.Count;

        public IServerProcess Start(ServerStartInfo info)
        {
            FakeProcess process = new();

            lock (_gate)
            {
                _processes.Add(process);
            }

            return process;
        }
    }

    // Stands in for the network and the model server: always healthy, and "translates" by putting a prefix
    // in front of the segment it finds at the end of the instruction.
    private sealed class FakeServer : HttpMessageHandler
    {
        private const string TextMarker = "Text:\n";

        private readonly Lock _gate = new();
        private readonly List<string> _translated = [];
        private int _atOnce;
        private int _mostAtOnce;

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public TimeSpan Delay { get; set; }

        /// <summary>When set, a translation request waits for this before it is answered.</summary>
        public Task? Hold { get; set; }

        /// <summary>Completed when a translation request arrives.</summary>
        public TaskCompletionSource Arrived { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MostAtOnce => Volatile.Read(ref _mostAtOnce);

        public IReadOnlyList<string> Translated
        {
            get
            {
                lock (_gate)
                {
                    return [.. _translated];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.RequestUri!.AbsolutePath == "/health")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            int atOnce = Interlocked.Increment(ref _atOnce);
            InterlockedMax(ref _mostAtOnce, atOnce);

            try
            {
                Arrived.TrySetResult();

                if (Hold is not null)
                {
                    await Hold.WaitAsync(cancellationToken);
                }

                if (Delay > TimeSpan.Zero)
                {
                    await Task.Delay(Delay, cancellationToken);
                }

                if (Status != HttpStatusCode.OK)
                {
                    return new HttpResponseMessage(Status);
                }

                string body = await request.Content!.ReadAsStringAsync(cancellationToken);
                string prompt = JsonNode.Parse(body)!["messages"]![0]!["content"]!.GetValue<string>();
                string text = prompt[(prompt.LastIndexOf(TextMarker, StringComparison.Ordinal) + TextMarker.Length)..];

                lock (_gate)
                {
                    _translated.Add(text);
                }

                JsonObject choice = new()
                {
                    ["finish_reason"] = "stop",
                    ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = "ru: " + text },
                };
                string answer = new JsonObject { ["choices"] = new JsonArray(choice) }.ToJsonString();

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(answer, Encoding.UTF8, "application/json"),
                };
            }
            finally
            {
                Interlocked.Decrement(ref _atOnce);
            }
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current = Volatile.Read(ref target);

            while (value > current)
            {
                int seen = Interlocked.CompareExchange(ref target, value, current);

                if (seen == current)
                {
                    return;
                }

                current = seen;
            }
        }
    }
}