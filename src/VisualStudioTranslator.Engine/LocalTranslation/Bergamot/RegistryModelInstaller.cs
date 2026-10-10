using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Rpc;

namespace VisualStudioTranslator.Engine.LocalTranslation.Bergamot;

/// <summary>
/// Installs a model from Mozilla's registry. Careful about three things, because it downloads
/// files from the network and puts them where the Engine will load them:
/// <list type="bullet">
/// <item>Nothing touches the network until <see cref="StartInstallAsync"/>, which is only called
/// after the user has agreed. Asking what is installed never does.</item>
/// <item>Where files come from is checked: https only, and only the host the registry itself is on.
/// Names and paths taken from the registry are validated before they are used for a URL or a file.</item>
/// <item>Nothing half-finished is ever left where a model is looked for. Everything is
/// built in a temporary folder beside the real ones and moved into place as the last step, so a failure,
/// or a crash, at any point leaves either a complete model or none.</item>
/// </list>
/// </summary>
internal sealed class RegistryModelInstaller(
    HttpClient http,
    LocalModelStore store,
    ITextTranslatorFactory factory,
    ILogger<RegistryModelInstaller> logger,
    TimeProvider? time = null,
    string? registryUrl = null) : IModelInstaller
{
    public const string DefaultRegistryUrl =
        "https://storage.googleapis.com/moz-fx-translations-data--303e-prod-translations-data/db/models.json";

    private static readonly TimeSpan RegistryTimeToLive = TimeSpan.FromHours(1);
    private static readonly TimeSpan RegistryTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(30);

    // A model file is tens of megabytes. Far more than that when unpacked is not a model.
    private const long MaxUnpackedBytes = 512L * 1024 * 1024;

    private readonly Uri _registryUri = new(registryUrl ?? DefaultRegistryUrl);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Job> _jobs = new();
    private readonly Lock _startGate = new();
    private readonly SemaphoreSlim _registryGate = new(1, 1);
    private JsonDocument? _registry;
    private DateTimeOffset _registryFetched;

    public Task<ModelInstallStatus> GetStatusAsync(LanguagePair pair, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string key = pair.ToString();
        _jobs.TryGetValue(key, out Job? job);

        if (job is { IsInstalling: true })
        {
            return Task.FromResult(job.Snapshot(_registryUri.Host));
        }

        if (factory.IsModelPresent(store.DirectoryFor(pair)))
        {
            return Task.FromResult(new ModelInstallStatus { State = ModelInstallState.Installed });
        }

        // A job that ended badly is remembered, so the client can say why; there is nothing else to report.
        return Task.FromResult(
            job?.Snapshot(_registryUri.Host)
            ?? new ModelInstallStatus { State = ModelInstallState.NotInstalled, SourceHost = _registryUri.Host });
    }

    public Task<ModelInstallStatus> StartInstallAsync(LanguagePair pair, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string key = pair.ToString();
        Job job;

        lock (_startGate)
        {
            if (_jobs.TryGetValue(key, out Job? running) && running.IsInstalling)
            {
                return Task.FromResult(running.Snapshot(_registryUri.Host));
            }

            if (factory.IsModelPresent(store.DirectoryFor(pair)))
            {
                return Task.FromResult(new ModelInstallStatus { State = ModelInstallState.Installed });
            }

            job = new Job();
            _jobs[key] = job;
        }

        // Not tied to the caller's token: the installation goes on after the call that asked for it has returned.
        _ = Task.Run(() => RunAsync(pair, job), CancellationToken.None);

        return Task.FromResult(job.Snapshot(_registryUri.Host));
    }

    // Runs in the background, so nothing may escape from it: an unhandled exception here would end the Engine.
    private async Task RunAsync(LanguagePair pair, Job job)
    {
        string? temporary = null;

        try
        {
            logger.ModelInstallStarted(pair);

            using CancellationTokenSource timeout = new(InstallTimeout);
            CancellationToken token = timeout.Token;

            RegistryModel? model = await ResolveAsync(pair, job, token).ConfigureAwait(false);
            if (model is null)
            {
                return;
            }

            temporary = Path.Combine(store.Root, LocalModelStore.InstallingPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);

            List<(RegistryFile File, Uri Url)> files = [.. model.Files.Select(file => (file, UrlOf(model, file)))];
            job.SetTotal(await MeasureAsync(files.Select(file => file.Url), token).ConfigureAwait(false));

            List<string> names = [];
            foreach ((RegistryFile file, Uri url) in files)
            {
                names.Add(await FetchAsync(file, url, temporary, job, token).ConfigureAwait(false));
            }

            string config = BergamotModelConfig.Build(names)
                ?? throw new InvalidDataException("The downloaded files do not make up a complete model.");
            await File.WriteAllTextAsync(Path.Combine(temporary, BergamotModelConfig.FileName), config, token).ConfigureAwait(false);

            Place(temporary, store.DirectoryFor(pair));
            temporary = null;

            store.NotifyChanged();
            _jobs.TryRemove(pair.ToString(), out _);
            logger.ModelInstalled(pair, model.Architecture);
        }
        catch (Exception exception)
        {
            logger.ModelInstallFailed(pair, exception);
            job.Finish(ModelInstallState.Failed, Describe(exception));
        }
        finally
        {
            if (temporary is not null)
            {
                TryDelete(temporary);
            }
        }
    }

    // Finds the model in the registry and checks it is somewhere it may be fetched from. On any
    // problem the job is finished with the reason and null comes back.
    private async Task<RegistryModel?> ResolveAsync(LanguagePair pair, Job job, CancellationToken cancellationToken)
    {
        JsonDocument? registry = await GetRegistryAsync(cancellationToken).ConfigureAwait(false);

        if (registry is null)
        {
            job.Finish(ModelInstallState.RegistryUnreachable, "The model server could not be reached.");
            return null;
        }

        RegistryModel? model = RegistryParser.Find(registry.RootElement, pair);

        if (model is null)
        {
            job.Finish(ModelInstallState.Unavailable, "No model is published for this language.");
            return null;
        }

        // Where a file may come from is decided by where the registry itself came from, not by what
        // the registry says about itself.
        if (model.BaseUrl.Scheme != Uri.UriSchemeHttps
            || !string.Equals(model.BaseUrl.Host, _registryUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            logger.RegistryHostRejected(model.BaseUrl.Host);
            job.Finish(ModelInstallState.Unavailable, "The model server did not offer a model that can be trusted.");
            return null;
        }

        return model;
    }

    private async Task<JsonDocument?> GetRegistryAsync(CancellationToken cancellationToken)
    {
        await _registryGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_registry is not null && _time.GetUtcNow() - _registryFetched < RegistryTimeToLive)
            {
                return _registry;
            }

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RegistryTimeout);

            try
            {
                await using Stream stream = await http.GetStreamAsync(_registryUri, timeout.Token).ConfigureAwait(false);
                JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);

                _registry?.Dispose();
                _registry = document;
                _registryFetched = _time.GetUtcNow();
                return document;
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException
                || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                logger.RegistryUnreachable(exception);

                // An older copy of the registry is still good for finding a model.
                return _registry;
            }
        }
        finally
        {
            _registryGate.Release();
        }
    }

    private static Uri UrlOf(RegistryModel model, RegistryFile file) =>
        new($"{model.BaseUrl.AbsoluteUri.TrimEnd('/')}/{file.Path}");

    // The total is only for showing progress, so a server that will not say is not a reason to stop.
    private async Task<long> MeasureAsync(IEnumerable<Uri> urls, CancellationToken cancellationToken)
    {
        long total = 0;

        try
        {
            foreach (Uri url in urls)
            {
                using HttpRequestMessage request = new(HttpMethod.Head, url);
                using HttpResponseMessage response = await http
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is not long length)
                {
                    return 0;
                }

                total += length;
            }
        }
        catch (HttpRequestException)
        {
            return 0;
        }

        return total;
    }

    // Downloads one file, unpacks it if it is compressed, checks it against the checksums the registry
    // gives, and returns its final name. A checksum may be for the file as downloaded or as unpacked.
    private async Task<string> FetchAsync(RegistryFile file, Uri url, string directory, Job job, CancellationToken cancellationToken)
    {
        string remoteName = Path.GetFileName(file.Path);
        string downloadPath = Path.Combine(directory, remoteName);

        using (HttpResponseMessage response = await http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();

            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using FileStream target = new(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            byte[] buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                job.AddDone(read);
            }
        }

        string downloadedHash = await HashFileAsync(downloadPath, cancellationToken).ConfigureAwait(false);
        string finalName = remoteName;
        string finalHash = downloadedHash;

        if (remoteName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            finalName = remoteName[..^3];
            finalHash = await UnpackAsync(downloadPath, Path.Combine(directory, finalName), cancellationToken).ConfigureAwait(false);
            File.Delete(downloadPath);
        }

        if (file.Sha256Hashes.Count == 0)
        {
            logger.FileUnverified(finalName);
        }
        else if (!file.Sha256Hashes.Contains(downloadedHash) && !file.Sha256Hashes.Contains(finalHash))
        {
            throw new InvalidDataException($"'{finalName}' does not match the checksum the registry gives for it.");
        }

        return finalName;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    // Returns the checksum of what came out. A file that is not valid gzip, or that unpacks to
    // something absurdly large, is rejected rather than allowed to fill the disk.
    private static async Task<string> UnpackAsync(string compressedPath, string destination, CancellationToken cancellationToken)
    {
        await using FileStream input = File.OpenRead(compressedPath);
        await using GZipStream gzip = new(input, CompressionMode.Decompress);
        await using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        byte[] buffer = new byte[81920];
        long total = 0;
        int read;

        while ((read = await gzip.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;

            if (total > MaxUnpackedBytes)
            {
                throw new InvalidDataException("A downloaded file unpacks to far more than any model.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // The target is only ever reached when no usable model is there (that is checked when the install
    // starts), so what it holds is an earlier incomplete attempt, and it is replaced.
    private static void Place(string source, string target)
    {
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        Directory.Move(source, target);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left behind, it is ignored by everything and removed with the next install's own cleanup of nothing: harmless.
        }
    }

    // What the user is told. Never the exception's own text: that can carry paths and addresses.
    private static string Describe(Exception exception) => exception switch
    {
        HttpRequestException => "The model could not be downloaded; check the network connection and try again.",
        InvalidDataException => "The downloaded model failed verification and was discarded.",
        IOException or UnauthorizedAccessException => "The model could not be saved to disk.",
        OperationCanceledException => "The download took too long and was stopped.",
        _ => "The model could not be installed.",
    };

    // One installation in progress, or the last one that did not succeed.
    private sealed class Job
    {
        private readonly Lock _gate = new();
        private ModelInstallState _state = ModelInstallState.Installing;
        private long _total;
        private long _done;
        private string? _detail;

        public bool IsInstalling
        {
            get
            {
                lock (_gate)
                {
                    return _state == ModelInstallState.Installing;
                }
            }
        }

        public void SetTotal(long total)
        {
            lock (_gate)
            {
                _total = total;
            }
        }

        public void AddDone(long bytes)
        {
            lock (_gate)
            {
                _done += bytes;
            }
        }

        public void Finish(ModelInstallState state, string detail)
        {
            lock (_gate)
            {
                _state = state;
                _detail = detail;
            }
        }

        public ModelInstallStatus Snapshot(string host)
        {
            lock (_gate)
            {
                return new ModelInstallStatus
                {
                    State = _state,
                    TotalBytes = _total,
                    DoneBytes = _done,
                    SourceHost = host,
                    Detail = _detail,
                };
            }
        }
    }
}