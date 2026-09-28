using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Vsix.Service;

namespace VisualStudioTranslator.Vsix;

/// <summary>
/// The extension's entry point. Loads in the background as soon as Visual Studio starts,
/// regardless of whether a solution is open, so its presence can be confirmed without
/// any other feature being wired up yet.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[InstalledProductRegistration(
    "Visual Studio Translator",
    "Translates IDE tooltips, XML documentation, and comments in place, without touching your source files.",
    "1.0")]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class VisualStudioTranslatorPackage : AsyncPackage
{
    public const string PackageGuidString = "5f2c9f7f-3f2e-4a7f-9b0e-1a2b3c4d5e6f";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        ActivityLog.LogInformation(nameof(VisualStudioTranslatorPackage), "Package initialized.");

        // Diagnostic only, for now: proves the Engine can be launched and spoken to over
        // the named pipe from inside devenv. No feature depends on this yet - the real
        // ServiceLauncher/ServiceClient lifecycle (single instance, idle shutdown) is a
        // later commit.
        await TryHandshakeWithEngineAsync(cancellationToken);
    }

    private static async Task TryHandshakeWithEngineAsync(CancellationToken cancellationToken)
    {
        try
        {
            ServiceLauncher.Start();

            string? userSid = WindowsIdentity.GetCurrent().User?.Value;
            string pipeName = PipeNaming.GetPipeName(userSid ?? "unknown");

            using ServiceClient client = await ServiceClient.ConnectAsync(pipeName, ConnectTimeout, cancellationToken);

            ClientInfo clientInfo = new()
            {
                ProductName = "Visual Studio",
                // Querying the real product version needs IVsShell; not needed to prove
                // the RPC round trip works, so left as a placeholder for now.
                VisualStudioVersion = "unknown",
                ExtensionVersion = typeof(VisualStudioTranslatorPackage).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                ProtocolMajor = ProtocolVersion.Major,
                ProtocolMinor = ProtocolVersion.Minor,
            };

            ServiceInfo serviceInfo = await client.Service.HandshakeAsync(clientInfo, cancellationToken);

            ActivityLog.LogInformation(
                nameof(VisualStudioTranslatorPackage),
                $"Engine handshake succeeded. Service version {serviceInfo.ServiceVersion}, protocol {serviceInfo.ProtocolMajor}.{serviceInfo.ProtocolMinor}.");
        }
        catch (Exception ex)
        {
            // A failed handshake must never take Visual Studio down with it.
            ActivityLog.LogError(nameof(VisualStudioTranslatorPackage), $"Engine handshake failed: {ex}");
        }
    }
}