using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.LocalTranslation;
using VisualStudioTranslator.Engine.LocalTranslation.Llama;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation;

// The configuration with the real installation, provider and server around it. No server is started: switching
// happens between requests, and only the program behind the server would be a stand-in.
public sealed class LocalModelConfigurationTests : IDisposable
{
    private const long GiB = 1024L * 1024 * 1024;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vst-local-model-" + Path.GetRandomFileName());
    private readonly LlamaInstallation _installation;
    private readonly LlamaServer _server;
    private readonly LlamaTranslationProvider _provider;
    private readonly LocalModelConfiguration _configuration;
    private readonly NoLauncher _launcher = new();

    // Never reached, since no server runs; disposed with the server.
    private readonly HttpClientHandler _transport = new();

    public LocalModelConfigurationTests()
    {
        // A machine with plenty of memory, which starts with the quality model.
        _installation = new LlamaInstallation(_root, LlamaCatalog.Runtime, LlamaCatalog.Quality);

        LlamaServerOptions options = new()
        {
            ExecutablePath = _installation.ServerPath,
            ModelPath = _installation.ModelPath,
            GpuLayers = LlamaCatalog.AllLayers,
        };

        _server = new LlamaServer(_launcher, options, _transport, NullLogger<LlamaServer>.Instance);
        _provider = new LlamaTranslationProvider(
            _server, options, new FakeTimeProvider(), NullLogger<LlamaTranslationProvider>.Instance);
        _configuration = new LocalModelConfiguration(
            _installation, _provider, 32 * GiB, NullLogger<LocalModelConfiguration>.Instance);
    }

    public void Dispose()
    {
        _configuration.Dispose();
        _provider.Dispose();
        _server.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApplyAsync_LightModel_IsWhatIsInstalledAndLoadedFromThenOn()
    {
        await _configuration.ApplyAsync(new LocalModelSettings { Model = LocalModelChoice.Light }, Token);

        _installation.Model.Should().BeSameAs(LlamaCatalog.Light);
        _server.ModelPath.Should().Be(_installation.ModelPath).And.EndWith(LlamaCatalog.Light.FileName);
        _provider.Info.Revision.Should().Contain(LlamaCatalog.Light.FileName);
    }

    [Fact]
    public async Task ApplyAsync_GraphicsCardTurnedOff_KeepsTheModelOnTheProcessor()
    {
        await _configuration.ApplyAsync(new LocalModelSettings { UseGraphicsCard = false }, Token);

        _server.GpuLayers.Should().Be(0);
        _installation.Model.Should().BeSameAs(LlamaCatalog.Quality);
    }

    [Fact]
    public async Task ApplyAsync_GraphicsCardTurnedBackOn_GivesTheCardEveryLayer()
    {
        await _configuration.ApplyAsync(new LocalModelSettings { UseGraphicsCard = false }, Token);

        await _configuration.ApplyAsync(new LocalModelSettings(), Token);

        _server.GpuLayers.Should().Be(LlamaCatalog.AllLayers);
    }

    [Fact]
    public async Task ApplyAsync_Automatic_GoesByTheMachinesMemory()
    {
        using LocalModelConfiguration small = new(
            _installation, _provider, 8 * GiB, NullLogger<LocalModelConfiguration>.Instance);

        await small.ApplyAsync(new LocalModelSettings { Model = LocalModelChoice.Quality }, Token);
        await small.ApplyAsync(new LocalModelSettings { Model = LocalModelChoice.Automatic }, Token);

        _installation.Model.Should().BeSameAs(LlamaCatalog.Light);
    }

    [Fact]
    public async Task ApplyAsync_TheSettingsTheEngineStartedWith_ChangeNothing()
    {
        string revision = _provider.Info.Revision;

        await _configuration.ApplyAsync(new LocalModelSettings(), Token);

        _installation.Model.Should().BeSameAs(LlamaCatalog.Quality);
        _server.GpuLayers.Should().Be(LlamaCatalog.AllLayers);
        _provider.Info.Revision.Should().Be(revision);
    }

    [Fact]
    public async Task ApplyAsync_NeverStartsTheServerOrDownloadsAnything()
    {
        await _configuration.ApplyAsync(new LocalModelSettings { Model = LocalModelChoice.Light }, Token);

        _launcher.Starts.Should().Be(0);
        Directory.Exists(_root).Should().BeFalse();
    }

    private sealed class NoLauncher : IServerProcessLauncher
    {
        public int Starts { get; private set; }

        public IServerProcess Start(ServerStartInfo info)
        {
            Starts++;
            throw new InvalidOperationException("No server is started in these tests.");
        }
    }
}