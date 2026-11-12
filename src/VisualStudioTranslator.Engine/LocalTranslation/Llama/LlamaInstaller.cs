using System.IO.Compression;
using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.LocalTranslation.Downloads;

namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>
/// Installs the server and the model the local provider needs. One model serves every language, so the
/// direction a caller asks about does not change the answer.
/// <list type="bullet">
/// <item>Nothing touches the network until <see cref="StartInstallAsync"/>, which is only called after the user
/// has agreed. Asking what is installed never does, and already says how much there is to download.</item>
/// <item>Every file is the one in <see cref="LlamaCatalog"/>, checked by size and checksum by
/// <see cref="FileDownloader"/>. The server's archive is unpacked into a folder of its own and moved into place
/// whole, so the Engine never finds half a server.</item>
/// <item>An installation that fails keeps what it had finished and what had arrived of the rest, so trying
/// again downloads only what is missing.</item>
/// <item>There is no limit on the time an installation takes: several gigabytes on a slow connection take
/// hours, and a download that has stopped moving is caught by the downloader.</item>
/// </list>
/// </summary>
internal sealed class LlamaInstaller(
    FileDownloader downloader, LlamaInstallation installation, ILogger<LlamaInstaller> logger) : IModelInstaller, IDisposable
{
    // The archive holds a few dozen programs and libraries, well under this.
    private const long MaxUnpackedBytes = 1024L * 1024 * 1024;

    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Job? _job;

    public Task<ModelInstallStatus> GetStatusAsync(LanguagePair pair, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(CurrentStatus());
        }
    }

    public Task<ModelInstallStatus> StartInstallAsync(LanguagePair pair, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Job job;

        lock (_gate)
        {
            if (_job is { IsInstalling: true } || installation.IsInstalled)
            {
                return Task.FromResult(CurrentStatus());
            }

            job = new Job(BytesToDownload());
            _job = job;
        }

        // Not tied to the caller's token: the installation goes on after the call that asked for it has returned.
        _ = Task.Run(() => RunAsync(job), CancellationToken.None);

        return Task.FromResult(job.Snapshot());
    }

    /// <summary>Stops an installation that is still running; what it had downloaded is kept for next time.</summary>
    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    // Called with the gate held.
    private ModelInstallStatus CurrentStatus()
    {
        if (_job is { IsInstalling: true })
        {
            return _job.Snapshot();
        }

        if (installation.IsInstalled)
        {
            return new ModelInstallStatus { State = ModelInstallState.Installed };
        }

        // A job that ended badly is remembered, so the client can say why.
        return _job?.Snapshot() ?? new ModelInstallStatus
        {
            State = ModelInstallState.NotInstalled,
            TotalBytes = BytesToDownload(),
            SourceHost = LlamaCatalog.SourceHosts,
        };
    }

    private long BytesToDownload() =>
        (installation.IsRuntimeInstalled ? 0 : installation.Runtime.Source.Size)
        + (installation.IsModelInstalled ? 0 : installation.Model.Source.Size);

    // Runs in the background, so nothing may escape from it: an unhandled exception here would end the Engine.
    private async Task RunAsync(Job job)
    {
        try
        {
            CancellationToken token = _shutdown.Token;
            logger.InstallStarted(installation.Model.Name);

            if (!installation.IsRuntimeInstalled)
            {
                await InstallRuntimeAsync(job, token).ConfigureAwait(false);
                logger.RuntimeInstalled(installation.Runtime.Version);
            }

            if (!installation.IsModelInstalled)
            {
                await downloader.DownloadAsync(
                    installation.Model.Source, installation.ModelPath, job.ProgressAfterFinished(), token).ConfigureAwait(false);
            }

            job.Finish(ModelInstallState.Installed, detail: null);
            logger.Installed(installation.Model.Name);
        }
        catch (Exception exception)
        {
            logger.InstallFailed(exception);
            job.Finish(ModelInstallState.Failed, Describe(exception));
        }
    }

    private async Task InstallRuntimeAsync(Job job, CancellationToken cancellationToken)
    {
        RuntimePackage runtime = installation.Runtime;
        string archive = Path.Combine(installation.DownloadsDirectory, runtime.ArchiveName);

        await downloader.DownloadAsync(runtime.Source, archive, job.ProgressAfterFinished(), cancellationToken).ConfigureAwait(false);

        string unpacking = Path.Combine(installation.RuntimesDirectory, LlamaInstallation.InstallingPrefix + Path.GetRandomFileName());

        try
        {
            Unpack(archive, unpacking);

            if (Directory.Exists(installation.RuntimeDirectory))
            {
                // Only reached when the server is missing from it, so it is an earlier attempt gone wrong.
                Directory.Delete(installation.RuntimeDirectory, recursive: true);
            }

            Directory.Move(unpacking, installation.RuntimeDirectory);
        }
        finally
        {
            TryDeleteDirectory(unpacking);
        }

        job.FinishPart(runtime.Source.Size);
        TryDeleteFile(archive);
        RemoveOtherBuilds();
    }

    // The archive is the pinned one, checked by its checksum, so this guards against a mistake in the catalogue
    // rather than an attack: files only, side by side, nothing that could land outside the folder.
    private static void Unpack(string archive, string directory)
    {
        Directory.CreateDirectory(directory);

        using ZipArchive zip = ZipFile.OpenRead(archive);
        long total = 0;

        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string name = entry.FullName;

            if (name.Length == 0 || !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)
                || name is "." or ".." || name.Contains(':', StringComparison.Ordinal))
            {
                throw new InvalidDataException("The server's archive holds something other than files side by side.");
            }

            total += entry.Length;

            if (total > MaxUnpackedBytes)
            {
                throw new InvalidDataException("The server's archive unpacks to far more than a server.");
            }

            entry.ExtractToFile(Path.Combine(directory, name));
        }

        if (!File.Exists(Path.Combine(directory, LlamaCatalog.ServerFileName)))
        {
            throw new InvalidDataException("The server's archive does not hold the server.");
        }
    }

    // Builds from before an update, and unpacking that a crash interrupted. Whatever is in use stays: Windows
    // refuses to delete a running program, and it goes the next time.
    private void RemoveOtherBuilds()
    {
        foreach (string directory in Directory.EnumerateDirectories(installation.RuntimesDirectory))
        {
            if (!string.Equals(directory, installation.RuntimeDirectory, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteDirectory(directory);
            }
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // In use, or locked by a scanner for a moment: harmless, and tried again after the next install.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An archive left in the downloads folder is only disk space; the next install replaces it.
        }
    }

    // What the user is told. Never the exception's own text: that can carry paths and addresses.
    private static string Describe(Exception exception) => exception switch
    {
        _ when FileDownloader.IsDiskFull(exception) => "There is not enough free disk space for the language model.",
        HttpRequestException => "The language model could not be downloaded; check the network connection and try again.",
        InvalidDataException => "The downloaded files failed verification and were discarded.",
        IOException or UnauthorizedAccessException => "The language model could not be saved to disk.",
        OperationCanceledException => "The download was stopped.",
        _ => "The language model could not be installed.",
    };

    // One installation in progress, or the last one that did not succeed.
    private sealed class Job(long total)
    {
        private readonly Lock _gate = new();
        private ModelInstallState _state = ModelInstallState.Installing;
        private long _finished;
        private long _current;
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

        /// <summary>Progress of the file being downloaded now, counted on top of the files already done.</summary>
        public IProgress<long> ProgressAfterFinished() => new FileProgress(this);

        public void FinishPart(long bytes)
        {
            lock (_gate)
            {
                _finished += bytes;
                _current = 0;
            }
        }

        public void Finish(ModelInstallState state, string? detail)
        {
            lock (_gate)
            {
                _state = state;
                _detail = detail;
            }
        }

        public ModelInstallStatus Snapshot()
        {
            lock (_gate)
            {
                return new ModelInstallStatus
                {
                    State = _state,
                    TotalBytes = total,
                    DoneBytes = _finished + _current,
                    SourceHost = LlamaCatalog.SourceHosts,
                    Detail = _detail,
                };
            }
        }

        private void SetCurrent(long bytes)
        {
            lock (_gate)
            {
                _current = bytes;
            }
        }

        private sealed class FileProgress(Job job) : IProgress<long>
        {
            public void Report(long value) => job.SetCurrent(value);
        }
    }
}