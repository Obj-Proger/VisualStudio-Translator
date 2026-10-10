using System.IO.Pipes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Core.Quality;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.Caching;
using VisualStudioTranslator.Engine.Lifecycle;
using VisualStudioTranslator.Engine.Rpc;
using VisualStudioTranslator.Engine.Tests.Translation;
using VisualStudioTranslator.Engine.Translation;
using VisualStudioTranslator.Engine.Tests.LocalTranslation;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.Rpc;

public sealed class NamedPipeRpcServerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ClientInfo CreateClientInfo() => new()
    {
        ProductName = "Test Harness",
        VisualStudioVersion = "0.0.0",
        ExtensionVersion = "0.0.0",
        ProtocolMajor = ProtocolVersion.Major,
        ProtocolMinor = ProtocolVersion.Minor,
    };

    [Fact]
    public async Task HandshakeAsync_RoundTripsOverNamedPipe()
    {
        await WithProxyAsync(
            [],
            async proxy =>
            {
                ServiceInfo result = await proxy.HandshakeAsync(CreateClientInfo(), Token);

                result.ProtocolMajor.Should().Be(ProtocolVersion.Major);
                result.ProtocolMinor.Should().Be(ProtocolVersion.Minor);
            });
    }

    [Fact]
    public async Task TranslateDocumentationAsync_RoundTripsOverNamedPipe()
    {
        // This is the test that proves the new request and result types survive the real
        // formatter: the glossary entry carries an enum, the result a nullable one.
        StubProvider provider = new(
            "local",
            translations: new Dictionary<string, string>
            {
                ["Returns the number of items."] = "Возвращает количество элементов.",
            });

        await WithProxyAsync(
            [provider],
            async proxy =>
            {
                TranslateDocumentationRequest request = new()
                {
                    DocumentationXml = "<member name=\"M:X\"><summary>Returns the number of items.</summary></member>",
                    SourceLanguage = "en",
                    TargetLanguage = "ru",
                    Glossary = [new GlossaryEntry { Term = "Roslyn", Kind = GlossaryEntryKind.DoNotTranslate }],
                };

                TranslateDocumentationResult result = await proxy.TranslateDocumentationAsync(request, Token);

                result.Outcome.Should().Be(TranslationOutcome.Completed);
                result.TranslatedCount.Should().Be(1);
                result.ProviderFailure.Should().BeNull();
                result.DocumentationXml.Should().Be(
                    "<member><summary><para>Возвращает количество элементов.</para></summary></member>");
            });
    }

    [Fact]
    public async Task ConnectedClient_IsCountedForExactlyAsLongAsTheConnectionLasts()
    {
        ClientTracker tracker = new(TimeProvider.System);

        await WithProxyAsync(
            [],
            async proxy =>
            {
                // A completed call proves the server has accepted the connection.
                await proxy.HandshakeAsync(CreateClientInfo(), Token);

                tracker.Connected.Should().Be(1);
            },
            tracker);

        await EventuallyAsync(() => tracker.Connected == 0);
    }

    // Starts a real server on the named pipe, connects a real client to it, and hands the
    // test a proxy for the contract. Tests in one class run one after another, so they can
    // safely share the pipe name.
    private static async Task WithProxyAsync(
    ITranslationProvider[] providers,
    Func<ITranslatorService, Task> test,
    ClientTracker? tracker = null,
    FakeModelInstaller? installer = null)
    {
        TranslatorService service = new(
            NullLogger<TranslatorService>.Instance,
            new TranslationOrchestrator(new MemoryTranslationCache(), NullLogger<TranslationOrchestrator>.Instance),
            providers,
            installer ?? new FakeModelInstaller());
        NamedPipeRpcServer server = new(
            service, tracker ?? new ClientTracker(TimeProvider.System), NullLogger<NamedPipeRpcServer>.Instance);

        await server.StartAsync(Token);
        try
        {
            await using NamedPipeClientStream client = new(
                ".", NamedPipeRpcServer.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5000, Token);

            using JsonRpc clientRpc = new(client, client);
            clientRpc.StartListening();

            await test(clientRpc.Attach<ITranslatorService>());
        }
        finally
        {
            await server.StopAsync(Token);
        }
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);

        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, Token);
        }

        condition().Should().BeTrue();
    }

    [Fact]
    public async Task GetModelStatusAsync_RoundTripsOverNamedPipe()
    {
        FakeModelInstaller installer = new()
        {
            Status = new ModelInstallStatus
            {
                State = ModelInstallState.Installing,
                TotalBytes = 41_000_000,
                DoneBytes = 12_345_678,
                SourceHost = "models.example.test",
                Detail = null,
            },
        };

        await WithProxyAsync(
            [],
            async proxy =>
            {
                ModelInstallStatus status = await proxy.GetModelStatusAsync("en", "ru", Token);

                status.State.Should().Be(ModelInstallState.Installing);
                status.TotalBytes.Should().Be(41_000_000);
                status.DoneBytes.Should().Be(12_345_678);
                status.SourceHost.Should().Be("models.example.test");
                status.Detail.Should().BeNull();
            },
            installer: installer);
    }
}