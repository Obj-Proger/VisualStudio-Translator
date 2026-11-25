using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using StreamJsonRpc;
using VisualStudioTranslator.Core.Rpc;

namespace VisualStudioTranslator.Vsix.Service;

/// <summary>
/// A connected RPC session with the Engine over its named pipe. Owns the pipe and the
/// StreamJsonRpc connection built on top of it for as long as this client is alive.
/// </summary>
internal sealed class ServiceClient : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly JsonRpc _jsonRpc;

    private ServiceClient(NamedPipeClientStream pipe, JsonRpc jsonRpc, ITranslatorService service)
    {
        _pipe = pipe;
        _jsonRpc = jsonRpc;
        Service = service;
    }

    public ITranslatorService Service { get; }

    public static async Task<ServiceClient> ConnectAsync(string pipeName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync((int)timeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
            EnsureOurEngine(pipe);
        }
        catch
        {
            pipe.Dispose();
            throw;
        }

        JsonRpc jsonRpc = new(pipe, pipe);
        jsonRpc.StartListening();
        ITranslatorService service = jsonRpc.Attach<ITranslatorService>();

        return new ServiceClient(pipe, jsonRpc, service);
    }

    public void Dispose()
    {
        _jsonRpc.Dispose();
        _pipe.Dispose();
    }

    // Before anything is sent: code from the editor goes down this pipe. Its name is only a convention, which
    // another account on the same machine could take first, so the pipe must have been made by this user, and the
    // process serving it must be an Engine this extension starts. The Engine checks the same of its clients
    // (PipeOptions.CurrentUserOnly).
    private static void EnsureOurEngine(NamedPipeClientStream pipe)
    {
        SecurityIdentifier? owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        SecurityIdentifier? user = WindowsIdentity.GetCurrent().User;

        if (owner is null || user is null || owner != user)
        {
            throw new UnauthorizedAccessException("The Engine's pipe was not created by the current user.");
        }

        if (!NativeMethods.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint processId))
        {
            throw new UnauthorizedAccessException(
                $"The process serving the Engine's pipe could not be told (error {Marshal.GetLastWin32Error()}).");
        }

        using Process server = Process.GetProcessById((int)processId);
        string? path = server.MainModule?.FileName;

        if (path is null || !ServiceLauncher.IsOurEngine(path))
        {
            throw new UnauthorizedAccessException($"The Engine's pipe is served by another program: {path ?? "unknown"}.");
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
    }
}