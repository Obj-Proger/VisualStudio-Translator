using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>A graphics card the server can compute on, as the server names it.</summary>
/// <param name="Id">What the server is told to use it by, such as "Vulkan0".</param>
/// <param name="Name">What it is, such as "NVIDIA GeForce RTX 4070 Laptop GPU".</param>
/// <param name="MemoryMiB">
/// The memory it reports. A card built into the processor reports a large share of the machine's own memory, so
/// this says nothing about which card is faster.
/// </param>
internal sealed record GpuDevice(string Id, string Name, long MemoryMiB);

/// <summary>Finds the graphics cards the installed server can use.</summary>
internal interface IDeviceLister
{
    /// <returns>The cards, none when there are none, or <see langword="null"/> when it could not be told.</returns>
    Task<IReadOnlyList<GpuDevice>?> ListAsync(string serverPath, CancellationToken cancellationToken);
}

/// <summary>Reads what <c>llama-server --list-devices</c> prints.</summary>
internal static partial class GpuDevices
{
    private const string Header = "Available devices:";

    /// <summary>
    /// The cards in what the server printed, in its order. The processor is not among them: it is always there.
    /// </summary>
    /// <returns>Null when the output is not a list of devices at all.</returns>
    public static IReadOnlyList<GpuDevice>? Parse(string output)
    {
        if (!output.Contains(Header, StringComparison.Ordinal))
        {
            return null;
        }

        return
        [
            .. DevicePattern().Matches(output).Select(match => new GpuDevice(
                match.Groups["id"].Value,
                match.Groups["name"].Value,
                long.Parse(match.Groups["total"].Value, CultureInfo.InvariantCulture))),
        ];
    }

    // "  Vulkan0: NVIDIA GeForce RTX 4070 Laptop GPU (7963 MiB, 7195 MiB free)"
    private const string DeviceLine =
        @"^\s*(?<id>[A-Za-z]+[0-9]+):\s*(?<name>.+?)\s*\((?<total>[0-9]+) MiB, [0-9]+ MiB free\)\s*$";

    [GeneratedRegex(DeviceLine, RegexOptions.Multiline)]
    private static partial Regex DevicePattern();
}

/// <summary>Asks the server itself, by starting it once with <c>--list-devices</c>, which takes a moment.</summary>
internal sealed class ProcessDeviceLister : IDeviceLister
{
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<GpuDevice>?> ListAsync(string serverPath, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(serverPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(serverPath) ?? string.Empty,
        };
        start.ArgumentList.Add("--list-devices");

        try
        {
            using Process process = Process.Start(start)
                ?? throw new InvalidOperationException("The server did not start.");
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ListTimeout);

            try
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

                return GpuDevices.Parse(await output.ConfigureAwait(false) + "\n" + await errors.ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                cancellationToken.ThrowIfCancellationRequested();
                return null;
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}