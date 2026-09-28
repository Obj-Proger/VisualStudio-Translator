using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
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
}