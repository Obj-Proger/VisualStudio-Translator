using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.LocalTranslation;
using VisualStudioTranslator.Engine.LocalTranslation.Bergamot;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Bergamot;

public sealed class RegistryModelInstallerTests : IDisposable
{
    private const string RegistryUrl = "https://models.example.test/db/models.json";
    private const string FilesUrl = "https://models.example.test/files";
    private const string ModelPath = "en-ru/model.enru.intgemm.alphas.bin.gz";
    private const string VocabPath = "en-ru/vocab.enru.spm.gz";
    private const string LexPath = "en-ru/lex.50.50.enru.s2t.bin.gz";

    private static readonly LanguagePair EnglishToRussian = LanguagePair.Create("en", "ru")!;
    private static readonly byte[] ModelBytes = Content(3000, 7);
    private static readonly byte[] VocabBytes = Content(800, 11);
    private static readonly byte[] LexBytes = Content(500, 13);

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("vst-install-");
    private readonly FakeServer _server = new();
    private readonly List<HttpClient> _clients = [];
    private readonly LocalModelStore _store;

    public RegistryModelInstallerTests() => _store = new LocalModelStore(_root.FullName);

    public void Dispose()
    {
        foreach (HttpClient client in _clients)
        {
            client.Dispose();
        }

        _root.Delete(recursive: true);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] Content(int length, int seed) =>
        [.. Enumerable.Range(0, length).Select(i => (byte)((i * seed) % 251))];

    private static byte[] Gzip(byte[] data)
    {
        using MemoryStream buffer = new();
        using (GZipStream gzip = new(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return buffer.ToArray();
    }

    private static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private RegistryModelInstaller Installer()
    {
        HttpClient client = new(_server);
        _clients.Add(client);

        return new RegistryModelInstaller(
            client, _store, new BlockingServiceTranslatorFactory(), NullLogger<RegistryModelInstaller>.Instance,
            registryUrl: RegistryUrl);
    }

    // Publishes a registry listing the three files of a model, each with an optional checksum field.
    private void Publish(string? modelHash = null, string baseUrl = FilesUrl)
    {
        string modelExtra = modelHash is null ? string.Empty : $""", "expectedSha256Hash": "{modelHash}" """;

        _server.Add(RegistryUrl, Encoding.UTF8.GetBytes($$"""
            { "baseUrl": "{{baseUrl}}", "models": { "en-ru": [ { "architecture": "base", "releaseStatus": "Release", "files": {
              "model": { "path": "{{ModelPath}}"{{modelExtra}} },
              "vocab": { "path": "{{VocabPath}}" },
              "lexicalShortlist": { "path": "{{LexPath}}" }
            } } ] } }
            """));

        PublishFiles();
    }

    private void PublishFiles()
    {
        _server.Add($"{FilesUrl}/{ModelPath}", Gzip(ModelBytes));
        _server.Add($"{FilesUrl}/{VocabPath}", Gzip(VocabBytes));
        _server.Add($"{FilesUrl}/{LexPath}", Gzip(LexBytes));
    }

    private static async Task<ModelInstallStatus> WaitForTheEndAsync(RegistryModelInstaller installer)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        ModelInstallStatus status;

        do
        {
            await Task.Delay(20, Token);
            status = await installer.GetStatusAsync(EnglishToRussian, Token);
        }
        while (status.State == ModelInstallState.Installing && DateTime.UtcNow < deadline);

        return status;
    }

    private string ModelDirectory => Path.Combine(_root.FullName, "en-ru");

    private IEnumerable<string> LeftoverTemporaryFolders() =>
        Directory.EnumerateDirectories(_root.FullName, LocalModelStore.InstallingPrefix + "*");

    // --- Asking what is installed ---

    [Fact]
    public async Task GetStatusAsync_NothingInstalled_IsNotInstalledAndTouchesNoNetwork()
    {
        Publish();
        RegistryModelInstaller installer = Installer();

        ModelInstallStatus status = await installer.GetStatusAsync(EnglishToRussian, Token);

        status.State.Should().Be(ModelInstallState.NotInstalled);
        status.SourceHost.Should().Be("models.example.test");
        _server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStatusAsync_AModelIsThere_IsInstalled()
    {
        Directory.CreateDirectory(ModelDirectory);
        File.WriteAllText(Path.Combine(ModelDirectory, "config.yml"), "relative-paths: true\n");

        (await Installer().GetStatusAsync(EnglishToRussian, Token)).State.Should().Be(ModelInstallState.Installed);
    }

    // --- Installing ---

    [Fact]
    public async Task StartInstallAsync_DownloadsUnpacksVerifiesAndInstalls()
    {
        Publish(modelHash: Sha256(ModelBytes));
        RegistryModelInstaller installer = Installer();
        int changeBefore = _store.ChangeCount;

        ModelInstallStatus started = await installer.StartInstallAsync(EnglishToRussian, Token);
        ModelInstallStatus finished = await WaitForTheEndAsync(installer);

        started.State.Should().Be(ModelInstallState.Installing);
        finished.State.Should().Be(ModelInstallState.Installed);
        File.ReadAllBytes(Path.Combine(ModelDirectory, "model.enru.intgemm.alphas.bin")).Should().Equal(ModelBytes);
        File.ReadAllBytes(Path.Combine(ModelDirectory, "vocab.enru.spm")).Should().Equal(VocabBytes);
        File.ReadAllBytes(Path.Combine(ModelDirectory, "lex.50.50.enru.s2t.bin")).Should().Equal(LexBytes);
        File.Exists(Path.Combine(ModelDirectory, "model.enru.intgemm.alphas.bin.gz")).Should().BeFalse();

        string config = File.ReadAllText(Path.Combine(ModelDirectory, "config.yml"));
        config.Should().Contain("model.enru.intgemm.alphas.bin");
        config.Should().Contain("gemm-precision: int8shiftAlphaAll");

        LeftoverTemporaryFolders().Should().BeEmpty();
        _store.ChangeCount.Should().BeGreaterThan(changeBefore);
    }

    [Fact]
    public async Task StartInstallAsync_WhileRunning_ReportsTheSizeAndJoinsInsteadOfStartingAnother()
    {
        Publish();
        TaskCompletionSource gate = _server.Hold($"{FilesUrl}/{ModelPath}");
        RegistryModelInstaller installer = Installer();

        await installer.StartInstallAsync(EnglishToRussian, Token);
        ModelInstallStatus second = await installer.StartInstallAsync(EnglishToRussian, Token);

        // Wait until the download is under way, held at the first file.
        ModelInstallStatus midway;
        do
        {
            await Task.Delay(20, Token);
            midway = await installer.GetStatusAsync(EnglishToRussian, Token);
        }
        while (midway.TotalBytes == 0);

        second.State.Should().Be(ModelInstallState.Installing);
        midway.State.Should().Be(ModelInstallState.Installing);
        midway.TotalBytes.Should().Be(Gzip(ModelBytes).Length + Gzip(VocabBytes).Length + Gzip(LexBytes).Length);

        gate.SetResult();
        (await WaitForTheEndAsync(installer)).State.Should().Be(ModelInstallState.Installed);
        _server.FileDownloads.Should().Be(3); // not 6
    }

    [Fact]
    public async Task StartInstallAsync_AlreadyInstalled_DownloadsNothing()
    {
        Directory.CreateDirectory(ModelDirectory);
        File.WriteAllText(Path.Combine(ModelDirectory, "config.yml"), "relative-paths: true\n");

        ModelInstallStatus status = await Installer().StartInstallAsync(EnglishToRussian, Token);

        status.State.Should().Be(ModelInstallState.Installed);
        _server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task StartInstallAsync_AChecksumOfTheCompressedFile_IsAccepted()
    {
        Publish(modelHash: Sha256(Gzip(ModelBytes)));
        RegistryModelInstaller installer = Installer();

        await installer.StartInstallAsync(EnglishToRussian, Token);

        (await WaitForTheEndAsync(installer)).State.Should().Be(ModelInstallState.Installed);
    }

    [Fact]
    public async Task StartInstallAsync_NoChecksumGiven_StillInstalls()
    {
        Publish(modelHash: null);
        RegistryModelInstaller installer = Installer();

        await installer.StartInstallAsync(EnglishToRussian, Token);

        (await WaitForTheEndAsync(installer)).State.Should().Be(ModelInstallState.Installed);
    }

    [Fact]
    public async Task StartInstallAsync_ChecksumThatDoesNotMatch_FailsAndLeavesNothingBehind()
    {
        Publish(modelHash: new string('a', 64));
        RegistryModelInstaller installer = Installer();

        await installer.StartInstallAsync(EnglishToRussian, Token);
        ModelInstallStatus status = await WaitForTheEndAsync(installer);

        status.State.Should().Be(ModelInstallState.Failed);
        status.Detail.Should().Contain("verification");
        Directory.Exists(ModelDirectory).Should().BeFalse();
        LeftoverTemporaryFolders().Should().BeEmpty();
    }

    [Fact]
    public async Task StartInstallAsync_FileThatIsNotThere_Fails()
    {
        Publish();
        _server.Remove($"{FilesUrl}/{VocabPath}");
        RegistryModelInstaller installer = Installer();

        await installer.StartInstallAsync(EnglishToRussian, Token);
        ModelInstallStatus status = await WaitForTheEndAsync(installer);

        status.State.Should().Be(ModelInstallState.Failed);
        Directory.Exists(ModelDirectory).Should().BeFalse();
        LeftoverTemporaryFolders().Should().BeEmpty();
    }

    [Fact]
    public async Task StartInstallAsync_FileThatIsNotValidGzip_Fails()
    {
        Publish();
        _server.Add($"{FilesUrl}/{VocabPath}", Encoding.UTF8.GetBytes("this is not gzip data at all"));
        RegistryModelInstaller installer = Installer();

        await installer.StartInstallAsync(EnglishToRussian, Token);

        (await WaitForTheEndAsync(installer)).State.Should().Be(ModelInstallState.Failed);
        LeftoverTemporaryFolders().Should().BeEmpty();
    }

    [Fact]
    public async Task StartInstallAsync_AfterAFailure_CanBeRetried()
    {
        Publish();
        _server.Remove($"{FilesUrl}/{VocabPath}");
        RegistryModelInstaller installer = Installer();
        await installer.StartInstallAsync(EnglishToRussian, Token);
        (await WaitForTheEndAsync(installer)).State.Should().Be(ModelInstallState.Failed);

        PublishFiles();
        await installer.StartInstallAsync(EnglishToRussian, Token);

        (await WaitForTheEndAsync(installer)).State.Should().Be(ModelInstallState.Installed);
    }

    [Fact]
    public async Task StartInstallAsync_AnIncompleteFolderFromAnEarlierAttempt_IsReplaced()
    {
        Directory.CreateDirectory(ModelDirectory);
        File.WriteAllText(Path.Combine(ModelDirectory, "stray.txt"), "left over");
        Publish();
        RegistryModelInstaller installer = Installer();

        await installer.StartInstallAsync(EnglishToRussian, Token);

        (await WaitForTheEndAsync(installer)).State.Should().Be(ModelInstallState.Installed);
        File.Exists(Path.Combine(ModelDirectory, "stray.txt")).Should().BeFalse();
    }

    // --- What the registry can say ---

    [Fact]
    public async Task StartInstallAsync_RegistryUnreachable_SaysSo()
    {
        _server.RegistryDown = true;
        RegistryModelInstaller installer = Installer();

        await installer.StartInstallAsync(EnglishToRussian, Token);
        ModelInstallStatus status = await WaitForTheEndAsync(installer);

        status.State.Should().Be(ModelInstallState.RegistryUnreachable);
        status.Detail.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task StartInstallAsync_NoModelForTheDirection_IsUnavailable()
    {
        Publish();
        RegistryModelInstaller installer = Installer();

        await installer.StartInstallAsync(LanguagePair.Create("en", "fr")!, Token);
        ModelInstallStatus status;
        do
        {
            await Task.Delay(20, Token);
            status = await installer.GetStatusAsync(LanguagePair.Create("en", "fr")!, Token);
        }
        while (status.State == ModelInstallState.Installing);

        status.State.Should().Be(ModelInstallState.Unavailable);
        _server.FileDownloads.Should().Be(0);
    }

    [Fact]
    public async Task StartInstallAsync_RegistryPointingAtAnotherHost_IsNotTrustedAndDownloadsNothing()
    {
        Publish(baseUrl: "https://elsewhere.example.test/files");
        RegistryModelInstaller installer = Installer();

        await installer.StartInstallAsync(EnglishToRussian, Token);
        ModelInstallStatus status = await WaitForTheEndAsync(installer);

        status.State.Should().Be(ModelInstallState.Unavailable);
        _server.FileDownloads.Should().Be(0);
        Directory.Exists(ModelDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task StartInstallAsync_RegistryOverPlainHttp_IsNotTrusted()
    {
        Publish(baseUrl: "http://models.example.test/files");
        RegistryModelInstaller installer = Installer();

        await installer.StartInstallAsync(EnglishToRussian, Token);

        (await WaitForTheEndAsync(installer)).State.Should().Be(ModelInstallState.Unavailable);
        _server.FileDownloads.Should().Be(0);
    }

    // A tiny stand-in for the model server: serves bytes by URL, can be held at a file, and counts what was asked.
    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files = [];
        private readonly Dictionary<string, TaskCompletionSource> _holds = [];
        private readonly Lock _gate = new();
        private int _fileDownloads;

        public bool RegistryDown { get; set; }

        public List<string> Requests { get; } = [];

        public int FileDownloads => Volatile.Read(ref _fileDownloads);

        public void Add(string url, byte[] content)
        {
            lock (_gate)
            {
                _files[url] = content;
            }
        }

        public void Remove(string url)
        {
            lock (_gate)
            {
                _files.Remove(url);
            }
        }

        public TaskCompletionSource Hold(string url)
        {
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_gate)
            {
                _holds[url] = gate;
            }

            return gate;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            byte[]? content;
            TaskCompletionSource? hold;

            lock (_gate)
            {
                Requests.Add($"{request.Method} {url}");
                _files.TryGetValue(url, out content);
                _holds.TryGetValue(url, out hold);
            }

            if (RegistryDown && url == RegistryUrl)
            {
                throw new HttpRequestException("The registry is down.");
            }

            if (content is null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Head)
            {
                HttpResponseMessage head = new(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
                head.Content.Headers.ContentLength = content.Length;
                return head;
            }

            if (url != RegistryUrl)
            {
                Interlocked.Increment(ref _fileDownloads);
            }

            if (hold is not null)
            {
                await hold.Task.WaitAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        }
    }
}