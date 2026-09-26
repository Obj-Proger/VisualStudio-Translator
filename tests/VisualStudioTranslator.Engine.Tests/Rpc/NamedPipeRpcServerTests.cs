using System.IO.Pipes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.Rpc;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.Rpc;

public sealed class NamedPipeRpcServerTests
{
    [Fact]
    public async Task HandshakeAsync_RoundTripsOverNamedPipe()
    {
        TranslatorService service = new(NullLogger<TranslatorService>.Instance);
        NamedPipeRpcServer server = new(service, NullLogger<NamedPipeRpcServer>.Instance);

        await server.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await using NamedPipeClientStream client = new(
                ".", NamedPipeRpcServer.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5000, TestContext.Current.CancellationToken);

            using JsonRpc clientRpc = new(client, client);
            clientRpc.StartListening();
            ITranslatorService proxy = clientRpc.Attach<ITranslatorService>();

            ClientInfo clientInfo = new()
            {
                ProductName = "Test Harness",
                VisualStudioVersion = "0.0.0",
                ExtensionVersion = "0.0.0",
                ProtocolMajor = ProtocolVersion.Major,
                ProtocolMinor = ProtocolVersion.Minor,
            };

            ServiceInfo result = await proxy.HandshakeAsync(clientInfo, TestContext.Current.CancellationToken);

            result.ProtocolMajor.Should().Be(ProtocolVersion.Major);
            result.ProtocolMinor.Should().Be(ProtocolVersion.Minor);
        }
        finally
        {
            await server.StopAsync(TestContext.Current.CancellationToken);
        }
    }
}