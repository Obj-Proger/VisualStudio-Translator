using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.LocalTranslation.Downloads;
using VisualStudioTranslator.Engine.LocalTranslation.Llama;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Llama;

// The installer with the real downloader; only the network is a stand-in, serving small files in place of the
// real ones.
public sealed class LlamaInstallerTests : IDisposable
{
    private static readonly LanguagePair EnglishToRussian = LanguagePair.Create("en", "ru")!;
    private static readonly Uri RuntimeAddress = new("https://github.example/llama-b1-bin-win-cpu-x64.zip");
    private static readonly Uri ModelAddress = new("https://models.example/model.gguf");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vst-llama-install-" + Path.GetRandomFileName());
    private readonly byte[] _model = Encoding.UTF8.GetBytes("a model of a few bytes");
    private readonly FakeNetwork _network = new();
    private readonly HttpClient _http;
    private long? _freeSpace;
    private byte[] _archive;

    public LlamaInstallerTests()
    {
        _archive = Archive(("llama-server.exe", "server"), ("llama.dll", "library"));
        _http = new HttpClient(_network) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public void Dispose()
    {
        // Disposes the fake network with it.
        _http.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] Archive(params (string Name, string Content)[] entries)
    {
        using MemoryStream stream = new();

        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                using StreamWriter writer = new(zip.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static DownloadSource SourceOf(Uri address, byte[] content) =>
        new(address, content.Length, Convert.ToHexStringLower(SHA256.HashData(content)));

    private LlamaInstallation Installation(string? modelChecksum = null)
    {
        _network.Files[RuntimeAddress] = _archive;
        _network.Files[ModelAddress] = _model;

        DownloadSource model = SourceOf(ModelAddress, _model);

        return new LlamaInstallation(
            _root,
            new RuntimePackage("b1", "llama-b1-bin-win-cpu-x64.zip", SourceOf(RuntimeAddress, _archive)),
            new ModelPackage("Test model", "model.gguf", modelChecksum is null ? model : model with { Sha256 = modelChecksum }));
    }

    private LlamaInstaller Installer(LlamaInstallation installation) => new(
        new FileDownloader(_http, freeSpace: _ => _freeSpace),
        installation,
        NullLogger<LlamaInstaller>.Instance);

    private static async Task<ModelInstallStatus> FinishedAsync(LlamaInstaller installer)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        ModelInstallStatus status = await installer.GetStatusAsync(EnglishToRussian, Token);

        while (status.State == ModelInstallState.Installing && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, Token);
            status = await installer.GetStatusAsync(EnglishToRussian, Token);
        }

        return status;
    }

    private void InstallRuntimeByHand(LlamaInstallation installation)
    {
        Directory.CreateDirectory(installation.RuntimeDirectory);
        File.WriteAllText(installation.ServerPath, "server");
    }

    [Fact]
    public async Task GetStatusAsync_NothingInstalled_SaysHowMuchThereIsToDownloadWithoutDownloading()
    {
        LlamaInstallation installation = Installation();
        using LlamaInstaller installer = Installer(installation);

        ModelInstallStatus status = await installer.GetStatusAsync(EnglishToRussian, Token);

        status.State.Should().Be(ModelInstallState.NotInstalled);
        status.TotalBytes.Should().Be(_archive.Length + _model.Length);
        status.SourceHost.Should().NotBeNullOrEmpty();
        _network.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task StartInstallAsync_InstallsTheServerAndTheModel()
    {
        LlamaInstallation installation = Installation();
        using LlamaInstaller installer = Installer(installation);

        await installer.StartInstallAsync(EnglishToRussian, Token);
        ModelInstallStatus status = await FinishedAsync(installer);

        status.State.Should().Be(ModelInstallState.Installed);
        File.ReadAllText(installation.ServerPath).Should().Be("server");
        File.ReadAllText(Path.Combine(installation.RuntimeDirectory, "llama.dll")).Should().Be("library");
        File.ReadAllBytes(installation.ModelPath).Should().Equal(_model);
        installation.IsInstalled.Should().BeTrue();
    }

    [Fact]
    public async Task StartInstallAsync_LeavesNoArchiveOrUnpackingFolderBehind()
    {
        LlamaInstallation installation = Installation();
        using LlamaInstaller installer = Installer(installation);

        await installer.StartInstallAsync(EnglishToRussian, Token);
        await FinishedAsync(installer);

        Directory.EnumerateFiles(installation.DownloadsDirectory).Should().BeEmpty();
        Directory.EnumerateDirectories(installation.RuntimesDirectory).Should().Equal(installation.RuntimeDirectory);
    }

    [Fact]
    public async Task StartInstallAsync_ReportsProgressWhileDownloading()
    {
        LlamaInstallation installation = Installation();
        using LlamaInstaller installer = Installer(installation);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _network.Hold[ModelAddress] = release.Task;

        await installer.StartInstallAsync(EnglishToRussian, Token);
        await _network.Arrived(ModelAddress).WaitAsync(Token);
        ModelInstallStatus during = await installer.GetStatusAsync(EnglishToRussian, Token);
        release.SetResult();
        await FinishedAsync(installer);

        during.State.Should().Be(ModelInstallState.Installing);
        during.TotalBytes.Should().Be(_archive.Length + _model.Length);
        during.DoneBytes.Should().Be(_archive.Length);
    }

    [Fact]
    public async Task StartInstallAsync_CalledAgainWhileRunning_JoinsTheRunningInstallation()
    {
        LlamaInstallation installation = Installation();
        using LlamaInstaller installer = Installer(installation);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _network.Hold[ModelAddress] = release.Task;

        await installer.StartInstallAsync(EnglishToRussian, Token);
        await _network.Arrived(ModelAddress).WaitAsync(Token);
        ModelInstallStatus second = await installer.StartInstallAsync(LanguagePair.Create("en", "de")!, Token);
        release.SetResult();
        await FinishedAsync(installer);

        second.State.Should().Be(ModelInstallState.Installing);
        _network.Requests.Should().Equal(RuntimeAddress, ModelAddress);
    }

    [Fact]
    public async Task StartInstallAsync_ServerAlreadyInstalled_DownloadsOnlyTheModel()
    {
        LlamaInstallation installation = Installation();
        InstallRuntimeByHand(installation);
        using LlamaInstaller installer = Installer(installation);

        (await installer.GetStatusAsync(EnglishToRussian, Token)).TotalBytes.Should().Be(_model.Length);

        await installer.StartInstallAsync(EnglishToRussian, Token);

        (await FinishedAsync(installer)).State.Should().Be(ModelInstallState.Installed);
        _network.Requests.Should().Equal(ModelAddress);
    }

    [Fact]
    public async Task StartInstallAsync_EverythingInstalled_SaysSoWithoutDownloading()
    {
        LlamaInstallation installation = Installation();
        InstallRuntimeByHand(installation);
        Directory.CreateDirectory(Path.GetDirectoryName(installation.ModelPath)!);
        File.WriteAllBytes(installation.ModelPath, _model);
        using LlamaInstaller installer = Installer(installation);

        ModelInstallStatus status = await installer.StartInstallAsync(EnglishToRussian, Token);

        status.State.Should().Be(ModelInstallState.Installed);
        _network.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task StartInstallAsync_ModelFailsVerification_KeepsTheServerAndRetriesOnlyTheModel()
    {
        using (LlamaInstaller failing = Installer(Installation(modelChecksum: new string('0', 64))))
        {
            await failing.StartInstallAsync(EnglishToRussian, Token);
            ModelInstallStatus failed = await FinishedAsync(failing);

            failed.State.Should().Be(ModelInstallState.Failed);
            failed.Detail.Should().Contain("verification");
        }

        LlamaInstallation installation = Installation();
        installation.IsRuntimeInstalled.Should().BeTrue();
        using LlamaInstaller installer = Installer(installation);

        await installer.StartInstallAsync(EnglishToRussian, Token);

        (await FinishedAsync(installer)).State.Should().Be(ModelInstallState.Installed);
        _network.Requests.Should().Equal(RuntimeAddress, ModelAddress, ModelAddress);
    }

    [Fact]
    public async Task StartInstallAsync_ArchiveWithAPathInIt_IsRefusedAndNothingLandsOutsideTheFolder()
    {
        _archive = Archive(("llama-server.exe", "server"), ("../escaped.dll", "library"));
        LlamaInstallation installation = Installation();
        using LlamaInstaller installer = Installer(installation);

        await installer.StartInstallAsync(EnglishToRussian, Token);
        ModelInstallStatus status = await FinishedAsync(installer);

        status.State.Should().Be(ModelInstallState.Failed);
        installation.IsRuntimeInstalled.Should().BeFalse();
        File.Exists(Path.Combine(installation.RuntimesDirectory, "escaped.dll")).Should().BeFalse();
        Directory.EnumerateDirectories(installation.RuntimesDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task StartInstallAsync_ArchiveWithoutTheServer_IsRefused()
    {
        _archive = Archive(("llama.dll", "library"));
        LlamaInstallation installation = Installation();
        using LlamaInstaller installer = Installer(installation);

        await installer.StartInstallAsync(EnglishToRussian, Token);

        (await FinishedAsync(installer)).State.Should().Be(ModelInstallState.Failed);
        installation.IsRuntimeInstalled.Should().BeFalse();
    }

    [Fact]
    public async Task StartInstallAsync_DownloadFails_SaysToCheckTheConnection()
    {
        LlamaInstallation installation = Installation();
        _network.Files.Remove(ModelAddress);
        using LlamaInstaller installer = Installer(installation);

        await installer.StartInstallAsync(EnglishToRussian, Token);
        ModelInstallStatus status = await FinishedAsync(installer);

        status.State.Should().Be(ModelInstallState.Failed);
        status.Detail.Should().Contain("network");
    }

    [Fact]
    public async Task StartInstallAsync_DiskTooFull_SaysSo()
    {
        _freeSpace = 0;
        LlamaInstallation installation = Installation();
        using LlamaInstaller installer = Installer(installation);

        await installer.StartInstallAsync(EnglishToRussian, Token);
        ModelInstallStatus status = await FinishedAsync(installer);

        status.State.Should().Be(ModelInstallState.Failed);
        status.Detail.Should().Contain("disk space");
        _network.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task StartInstallAsync_RemovesBuildsLeftFromBefore()
    {
        LlamaInstallation installation = Installation();
        string older = Path.Combine(installation.RuntimesDirectory, "b0");
        Directory.CreateDirectory(older);
        File.WriteAllText(Path.Combine(older, "llama-server.exe"), "older server");
        using LlamaInstaller installer = Installer(installation);

        await installer.StartInstallAsync(EnglishToRussian, Token);
        await FinishedAsync(installer);

        Directory.Exists(older).Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_AfterAFailure_SaysWhy()
    {
        LlamaInstallation installation = Installation(modelChecksum: new string('0', 64));
        using LlamaInstaller installer = Installer(installation);

        await installer.StartInstallAsync(EnglishToRussian, Token);
        await FinishedAsync(installer);

        ModelInstallStatus status = await installer.GetStatusAsync(EnglishToRussian, Token);

        status.State.Should().Be(ModelInstallState.Failed);
        status.Detail.Should().NotBeNullOrEmpty();
    }

    // Serves whole files by address and remembers which were asked for.
    private sealed class FakeNetwork : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly List<Uri> _requests = [];
        private readonly Dictionary<Uri, TaskCompletionSource> _arrived = [];

        public Dictionary<Uri, byte[]> Files { get; } = [];

        /// <summary>Requests for these addresses wait for the task before they are answered.</summary>
        public Dictionary<Uri, Task> Hold { get; } = [];

        public IReadOnlyList<Uri> Requests
        {
            get
            {
                lock (_gate)
                {
                    return [.. _requests];
                }
            }
        }

        public Task Arrived(Uri address) => Signal(address).Task;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri address = request.RequestUri!;

            lock (_gate)
            {
                _requests.Add(address);
            }

            Signal(address).TrySetResult();

            if (Hold.TryGetValue(address, out Task? hold))
            {
                await hold.WaitAsync(cancellationToken);
            }

            if (!Files.TryGetValue(address, out byte[]? content))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        }

        private TaskCompletionSource Signal(Uri address)
        {
            lock (_gate)
            {
                if (!_arrived.TryGetValue(address, out TaskCompletionSource? signal))
                {
                    signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _arrived[address] = signal;
                }

                return signal;
            }
        }
    }
}