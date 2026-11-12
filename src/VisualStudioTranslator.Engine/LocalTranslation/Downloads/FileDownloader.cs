using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace VisualStudioTranslator.Engine.LocalTranslation.Downloads;

/// <summary>A file to download, and what it must turn out to be.</summary>
/// <param name="Url">Where it is; https only.</param>
/// <param name="Size">Its exact length in bytes.</param>
/// <param name="Sha256">Its SHA-256 checksum, as 64 hexadecimal digits.</param>
internal sealed record DownloadSource(Uri Url, long Size, string Sha256);

/// <summary>
/// Downloads one large file, such as a model of several gigabytes, so that it can be trusted and so that an
/// interrupted download is not lost.
/// <list type="bullet">
/// <item>The file is only accepted if it has exactly the size and checksum it was published with. Those come
/// from the Engine's own list, not from the server, so a file changed or corrupted anywhere on the way is
/// refused. That is also why following redirects, which download sites use to send the request on to their
/// storage, is safe: where the bytes come from does not matter once their checksum is known.</item>
/// <item>The bytes go to a <see cref="PartialSuffix"/> file beside the destination, and the destination appears
/// only once they are complete and verified, under its final name in one step.</item>
/// <item>A download that stops, because the connection drops, stalls or is cancelled, keeps what arrived. The
/// next attempt asks the server for the rest only, so a dropped connection near the end of a large file costs
/// seconds, not the whole download again.</item>
/// <item>The disk is checked for room first, and never more than the expected size is written.</item>
/// </list>
/// </summary>
internal sealed class FileDownloader
{
    public const string PartialSuffix = ".part";

    /// <summary>The <see cref="Exception.HResult"/> of a full disk (ERROR_DISK_FULL), as Windows reports it.</summary>
    public const int DiskFullHResult = unchecked((int)0x80070070);

    private const int ErrorHandleDiskFullHResult = unchecked((int)0x80070027);
    private const int BufferSize = 128 * 1024;

    // Left free after the download, so that finishing one does not leave the system without room to work.
    private const long SpareBytes = 256L * 1024 * 1024;

    private static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromMinutes(1);

    private readonly HttpClient _http;
    private readonly TimeSpan _stallTimeout;
    private readonly Func<string, long?> _freeSpace;

    /// <param name="http">Without an overall timeout: a large download takes as long as it takes. Stalls are caught here.</param>
    /// <param name="stallTimeout">How long the server may send nothing before the download counts as broken.</param>
    /// <param name="freeSpace">Room left on the disk that holds a path, or null when it cannot be told; for tests.</param>
    public FileDownloader(HttpClient http, TimeSpan? stallTimeout = null, Func<string, long?>? freeSpace = null)
    {
        _http = http;
        _stallTimeout = stallTimeout ?? DefaultStallTimeout;
        _freeSpace = freeSpace ?? FreeSpaceOf;
    }

    /// <summary>Whether an exception means the disk is full, whether found before downloading or while writing.</summary>
    public static bool IsDiskFull(Exception exception) =>
        exception is IOException { HResult: DiskFullHResult or ErrorHandleDiskFullHResult };

    /// <summary>
    /// Downloads <paramref name="source"/> to <paramref name="destination"/>, carrying on from an earlier attempt
    /// if one left part of it. An existing destination is replaced.
    /// </summary>
    /// <param name="progress">Told how many bytes of the file are on disk, counting what earlier attempts left.</param>
    /// <exception cref="HttpRequestException">
    /// The server refused, or the connection dropped or stalled. What arrived is kept.
    /// </exception>
    /// <exception cref="InvalidDataException">The file is not the one expected. Nothing of it is kept.</exception>
    /// <exception cref="IOException">The file could not be written; see <see cref="IsDiskFull"/>.</exception>
    public async Task DownloadAsync(
        DownloadSource source, string destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        Validate(source);

        string target = Path.GetFullPath(destination);
        string partial = target + PartialSuffix;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        long existing = LengthOf(partial);

        if (existing > source.Size)
        {
            // Not a beginning of this file; nothing in it can be used.
            File.Delete(partial);
            existing = 0;
        }

        EnsureRoom(target, source.Size - existing);

        try
        {
            if (existing < source.Size)
            {
                await FetchRestAsync(source, partial, existing, progress, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // An earlier attempt got every byte and stopped before checking them.
                progress?.Report(existing);
            }

            if (!await HasChecksumAsync(partial, source.Sha256, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException("The downloaded file does not match its published checksum.");
            }
        }
        catch (InvalidDataException)
        {
            // Resuming from bytes that are known to be wrong would only give the same wrong file.
            TryDelete(partial);
            throw;
        }

        File.Move(partial, target, overwrite: true);
    }

    private static void Validate(DownloadSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Files are only downloaded over https.", nameof(source));
        }

        if (source.Size <= 0)
        {
            throw new ArgumentException("The expected size must be positive.", nameof(source));
        }

        if (source.Sha256.Length != 64 || !source.Sha256.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("The checksum must be 64 hexadecimal digits.", nameof(source));
        }
    }

    private async Task FetchRestAsync(
        DownloadSource source, string partial, long existing, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        using CancellationTokenSource stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        HttpResponseMessage response = await SendAsync(source.Url, existing, stall, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existing > 0)
        {
            // The server's file does not go on where ours stops. Its checksum would decide in the end anyway;
            // starting over decides now.
            response.Dispose();
            File.Delete(partial);
            existing = 0;
            response = await SendAsync(source.Url, existing, stall, cancellationToken).ConfigureAwait(false);
        }

        using (response)
        {
            response.EnsureSuccessStatusCode();

            long offset = StartOf(response, source, existing);
            progress?.Report(offset);

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using FileStream file = new(
                partial,
                offset == 0 ? FileMode.Create : FileMode.Append,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true);

            await CopyAsync(body, file, offset, source.Size, stall, progress, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        Uri url, long from, CancellationTokenSource stall, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);

        if (from > 0)
        {
            request.Headers.Range = new RangeHeaderValue(from, null);
        }

        stall.CancelAfter(_stallTimeout);

        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException("The server did not answer.", exception);
        }
    }

    // Where in the file this response begins, checked against what is expected of it.
    private static long StartOf(HttpResponseMessage response, DownloadSource source, long requested)
    {
        long offset = 0;

        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            ContentRangeHeaderValue? range = response.Content.Headers.ContentRange;

            if (range is null || range.From != requested || (range.Length is long total && total != source.Size))
            {
                throw new InvalidDataException("The server resumed the download somewhere other than asked.");
            }

            offset = requested;
        }

        // A server that ignored the request for the rest sends the whole file, which is just as good.
        if (response.Content.Headers.ContentLength is long length && offset + length != source.Size)
        {
            throw new InvalidDataException("The server offers a file of another size than the one expected.");
        }

        return offset;
    }

    private async Task CopyAsync(
        Stream body,
        FileStream file,
        long offset,
        long size,
        CancellationTokenSource stall,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[BufferSize];
        long done = offset;

        while (true)
        {
            int read;
            stall.CancelAfter(_stallTimeout);

            try
            {
                read = await body.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new HttpRequestException("The download stalled.", exception);
            }
            catch (IOException exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new HttpRequestException("The connection was lost during the download.", exception);
            }

            if (read == 0)
            {
                break;
            }

            if (done + read > size)
            {
                throw new InvalidDataException("The server sent more than the file's size.");
            }

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            done += read;
            progress?.Report(done);
        }

        await file.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (done < size)
        {
            throw new HttpRequestException("The connection ended before the whole file had arrived.");
        }
    }

    private static async Task<bool> HasChecksumAsync(string path, string expected, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);

        return string.Equals(Convert.ToHexStringLower(hash), expected, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureRoom(string target, long needed)
    {
        if (_freeSpace(target) is long available && available < needed + SpareBytes)
        {
            throw new IOException("There is not enough free disk space for the download.", DiskFullHResult);
        }
    }

    private static long? FreeSpaceOf(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(path);
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A network share, say: the write itself will tell if it runs out of room.
            return null;
        }
    }

    private static long LengthOf(string path)
    {
        FileInfo file = new(path);
        return file.Exists ? file.Length : 0;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left behind, it is found too long or wrong next time and replaced then.
        }
    }
}