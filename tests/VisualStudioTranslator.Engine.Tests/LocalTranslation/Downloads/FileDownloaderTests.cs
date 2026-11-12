using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using AwesomeAssertions;
using VisualStudioTranslator.Engine.LocalTranslation.Downloads;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Downloads;

public sealed class FileDownloaderTests : IDisposable
{
    private static readonly Uri Address = new("https://models.example/model.gguf");

    private readonly byte[] _content = new byte[300_000];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vst-download-" + Path.GetRandomFileName());
    private readonly string _destination;
    private readonly FakeFileServer _server;
    private readonly HttpClient _http;

    public FileDownloaderTests()
    {
        // Bytes that do not repeat in step with any buffer size, so a piece out of place shows.
        for (int i = 0; i < _content.Length; i++)
        {
            _content[i] = (byte)((i * 31) + (i / 251));
        }

        _destination = Path.Combine(_directory, "model.gguf");
        _server = new FakeFileServer(_content);
        _http = new HttpClient(_server) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public void Dispose()
    {
        // Disposes the fake server with it.
        _http.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Partial => _destination + FileDownloader.PartialSuffix;

    private DownloadSource Source => new(Address, _content.Length, Convert.ToHexStringLower(SHA256.HashData(_content)));

    private FileDownloader Downloader(TimeSpan? stallTimeout = null, long? freeSpace = null) =>
        new(_http, stallTimeout, freeSpace is null ? null : _ => freeSpace);

    private Task DownloadAsync(
        FileDownloader? downloader = null, DownloadSource? source = null, IProgress<long>? progress = null) =>
        (downloader ?? Downloader()).DownloadAsync(source ?? Source, _destination, progress, Token);

    private void LeavePartial(int length)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(Partial, _content[..length]);
    }

    [Fact]
    public async Task DownloadAsync_SavesTheFileUnderItsNameAndLeavesNoPartialFile()
    {
        await DownloadAsync();

        File.ReadAllBytes(_destination).Should().Equal(_content);
        File.Exists(Partial).Should().BeFalse();
        _server.Requests.Should().ContainSingle().Which.Should().BeNull();
    }

    [Fact]
    public async Task DownloadAsync_ReportsProgressUpToTheWholeFile()
    {
        RecordingProgress progress = new();

        await DownloadAsync(progress: progress);

        progress.Values.Should().BeInAscendingOrder();
        progress.Values[^1].Should().Be(_content.Length);
    }

    [Fact]
    public async Task DownloadAsync_PartLeftByAnEarlierAttempt_AsksOnlyForTheRest()
    {
        LeavePartial(100_000);

        await DownloadAsync();

        File.ReadAllBytes(_destination).Should().Equal(_content);
        _server.Requests.Single()!.Ranges.Single().From.Should().Be(100_000);
        _server.BytesSent.Should().Be(_content.Length - 100_000);
    }

    [Fact]
    public async Task DownloadAsync_ServerIgnoresTheRequestForTheRest_TakesTheWholeFileInstead()
    {
        LeavePartial(100_000);
        _server.IgnoreRange = true;

        await DownloadAsync();

        File.ReadAllBytes(_destination).Should().Equal(_content);
    }

    [Fact]
    public async Task DownloadAsync_ServerCannotGoOnFromThePart_StartsOver()
    {
        LeavePartial(100_000);
        _server.RejectRangeOnce = true;

        await DownloadAsync();

        File.ReadAllBytes(_destination).Should().Equal(_content);
        _server.Requests.Should().HaveCount(2);
        _server.Requests[1].Should().BeNull();
    }

    [Fact]
    public async Task DownloadAsync_PartLongerThanTheFile_IsThrownAway()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(Partial, new byte[_content.Length + 1]);

        await DownloadAsync();

        File.ReadAllBytes(_destination).Should().Equal(_content);
        _server.Requests.Single().Should().BeNull();
    }

    [Fact]
    public async Task DownloadAsync_PartAlreadyComplete_IsCheckedAndUsedWithoutAsking()
    {
        LeavePartial(_content.Length);

        await DownloadAsync();

        File.ReadAllBytes(_destination).Should().Equal(_content);
        _server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadAsync_ConnectionDrops_KeepsWhatArrivedAndTheNextAttemptFinishes()
    {
        _server.DropAfterOnce = 120_000;

        Func<Task> first = () => DownloadAsync();

        await first.Should().ThrowAsync<HttpRequestException>();
        File.Exists(_destination).Should().BeFalse();
        new FileInfo(Partial).Length.Should().Be(120_000);

        await DownloadAsync();

        File.ReadAllBytes(_destination).Should().Equal(_content);
        _server.Requests[^1]!.Ranges.Single().From.Should().Be(120_000);
    }

    [Fact]
    public async Task DownloadAsync_ServerStopsSendingAnything_FailsAndKeepsWhatArrived()
    {
        _server.StallAfterOnce = 50_000;

        Func<Task> act = () => DownloadAsync(Downloader(stallTimeout: TimeSpan.FromMilliseconds(200)));

        await act.Should().ThrowAsync<HttpRequestException>();
        new FileInfo(Partial).Length.Should().Be(50_000);
    }

    [Fact]
    public async Task DownloadAsync_Cancelled_ThrowsAndKeepsWhatArrived()
    {
        _server.StallAfterOnce = 50_000;
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        _server.Stalled = cancellation.Cancel;

        Func<Task> act = () => Downloader().DownloadAsync(Source, _destination, null, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        new FileInfo(Partial).Length.Should().Be(50_000);
    }

    [Fact]
    public async Task DownloadAsync_ChecksumDiffers_RejectsTheFileAndKeepsNothing()
    {
        DownloadSource wrong = Source with { Sha256 = new string('0', 64) };

        Func<Task> act = () => DownloadAsync(source: wrong);

        await act.Should().ThrowAsync<InvalidDataException>();
        File.Exists(_destination).Should().BeFalse();
        File.Exists(Partial).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_ServerOffersAnotherSize_RejectsItAndKeepsNothing()
    {
        DownloadSource smaller = Source with { Size = _content.Length - 1 };

        Func<Task> act = () => DownloadAsync(source: smaller);

        await act.Should().ThrowAsync<InvalidDataException>();
        File.Exists(Partial).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_ServerSendsMoreThanItSaid_StopsAtTheExpectedSize()
    {
        _server.HideLength = true;
        DownloadSource smaller = Source with { Size = _content.Length - 1 };

        Func<Task> act = () => DownloadAsync(source: smaller);

        await act.Should().ThrowAsync<InvalidDataException>();
        File.Exists(Partial).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_FileMissingOnTheServer_Fails()
    {
        _server.Status = HttpStatusCode.NotFound;

        Func<Task> act = () => DownloadAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
        File.Exists(_destination).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_NotEnoughRoomOnTheDisk_FailsBeforeDownloadingAnything()
    {
        Func<Task> act = () => DownloadAsync(Downloader(freeSpace: _content.Length));

        (await act.Should().ThrowAsync<IOException>()).Which.HResult.Should().Be(FileDownloader.DiskFullHResult);
        _server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadAsync_PlainHttp_IsRefusedWithoutAsking()
    {
        DownloadSource insecure = Source with { Url = new Uri("http://models.example/model.gguf") };

        Func<Task> act = () => DownloadAsync(source: insecure);

        await act.Should().ThrowAsync<ArgumentException>();
        _server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadAsync_ReplacesAFileAlreadyThere()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_destination, "an older file");

        await DownloadAsync();

        File.ReadAllBytes(_destination).Should().Equal(_content);
    }

    [Fact]
    public void IsDiskFull_OtherInputOutputErrors_AreNot()
    {
        FileDownloader.IsDiskFull(new IOException("The file is in use.")).Should().BeFalse();
        FileDownloader.IsDiskFull(new InvalidDataException()).Should().BeFalse();
    }

    private sealed class RecordingProgress : IProgress<long>
    {
        public List<long> Values { get; } = [];

        public void Report(long value) => Values.Add(value);
    }

    // Serves one file, honoring requests for a range of it unless told not to. Each "Once" setting applies to the
    // next response only.
    private sealed class FakeFileServer(byte[] content) : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly List<RangeHeaderValue?> _requests = [];
        private long _bytesSent;

        public HttpStatusCode? Status { get; set; }

        public bool IgnoreRange { get; set; }

        public bool RejectRangeOnce { get; set; }

        /// <summary>Leaves out the length, so a client only finds out how much comes by reading it.</summary>
        public bool HideLength { get; set; }

        public int? DropAfterOnce { get; set; }

        public int? StallAfterOnce { get; set; }

        /// <summary>Called when a response stops sending.</summary>
        public Action? Stalled { get; set; }

        public long BytesSent => Interlocked.Read(ref _bytesSent);

        public IReadOnlyList<RangeHeaderValue?> Requests
        {
            get
            {
                lock (_gate)
                {
                    return [.. _requests];
                }
            }
        }

        public void CountSent(int bytes) => Interlocked.Add(ref _bytesSent, bytes);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RangeHeaderValue? range = request.Headers.Range;

            lock (_gate)
            {
                _requests.Add(range);
            }

            if (Status is HttpStatusCode status)
            {
                return Task.FromResult(new HttpResponseMessage(status));
            }

            if (range is not null && RejectRangeOnce)
            {
                RejectRangeOnce = false;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
            }

            int from = range is not null && !IgnoreRange ? (int)(range.Ranges.Single().From ?? 0) : 0;

            ServingStream body = new(this, content[from..], DropAfterOnce, StallAfterOnce);
            DropAfterOnce = null;
            StallAfterOnce = null;

            HttpResponseMessage response = new(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new StreamContent(body),
            };

            if (!HideLength)
            {
                response.Content.Headers.ContentLength = content.Length - from;
            }

            if (from > 0)
            {
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, content.Length - 1, content.Length);
            }

            return Task.FromResult(response);
        }
    }

    // A response body that can break off: by failing, as a dropped connection does, or by going quiet.
    private sealed class ServingStream(FakeFileServer server, byte[] data, int? dropAfter, int? stallAfter) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position == dropAfter)
            {
                throw new IOException("The connection was reset.");
            }

            if (_position == stallAfter)
            {
                server.Stalled?.Invoke();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            int end = Math.Min(data.Length, Math.Min(dropAfter ?? int.MaxValue, stallAfter ?? int.MaxValue));
            int count = Math.Min(Math.Min(buffer.Length, 8192), end - _position);

            data.AsSpan(_position, count).CopyTo(buffer.Span);
            _position += count;
            server.CountSent(count);

            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}