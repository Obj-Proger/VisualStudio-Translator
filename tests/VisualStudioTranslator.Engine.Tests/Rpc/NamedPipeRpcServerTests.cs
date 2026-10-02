using System.IO.Pipes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Core.Quality;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.Caching;
using VisualStudioTranslator.Engine.Rpc;
using VisualStudioTranslator.Engine.Tests.Translation;
using VisualStudioTranslator.Engine.Translation;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.Rpc;

public sealed class NamedPipeRpcServerTests
{
    [Fact]
    public async Task HandshakeAsync_RoundTripsOverNamedPipe()
    {
        await WithProxyAsync(
            [],
            async proxy =>
            {
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

                TranslateDocumentationResult result =
                    await proxy.TranslateDocumentationAsync(request, TestContext.Current.CancellationToken);

                result.Outcome.Should().Be(TranslationOutcome.Completed);
                result.TranslatedCount.Should().Be(1);
                result.ProviderFailure.Should().BeNull();
                result.DocumentationXml.Should().Be(
                    "<member><summary><para>Возвращает количество элементов.</para></summary></member>");
            });
    }

    // Starts a real server on the named pipe, connects a real client to it, and hands the
    // test a proxy for the contract. Tests in one class run one after another, so they can
    // safely share the pipe name.
    private static async Task WithProxyAsync(ITranslationProvider[] providers, Func<ITranslatorService, Task> test)
    {
        TranslatorService service = new(
            NullLogger<TranslatorService>.Instance,
            new TranslationOrchestrator(new MemoryTranslationCache(), NullLogger<TranslationOrchestrator>.Instance),
            providers);
        NamedPipeRpcServer server = new(service, NullLogger<NamedPipeRpcServer>.Instance);

        await server.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await using NamedPipeClientStream client = new(
                ".", NamedPipeRpcServer.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5000, TestContext.Current.CancellationToken);

            using JsonRpc clientRpc = new(client, client);
            clientRpc.StartListening();

            await test(clientRpc.Attach<ITranslatorService>());
        }
        finally
        {
            await server.StopAsync(TestContext.Current.CancellationToken);
        }
    }
}