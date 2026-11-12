using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Engine.LocalTranslation.Llama;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Llama;

public sealed class ChatCompletionTranslatorTests : IDisposable
{
    private static readonly LanguagePair EnglishToRussian = LanguagePair.Create("en", "ru")!;

    private readonly FakeServer _server = new();
    private readonly HttpClient _client;
    private readonly ChatCompletionTranslator _translator;

    public ChatCompletionTranslatorTests()
    {
        _client = new HttpClient(_server) { BaseAddress = new Uri("http://127.0.0.1:8080/") };
        _translator = new ChatCompletionTranslator(_client);
    }

    // Disposes the fake server with it.
    public void Dispose() => _client.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Task<string> TranslateAsync(string text) => _translator.TranslateAsync(EnglishToRussian, text, Token);

    private JsonNode SentBody() => JsonNode.Parse(_server.RequestBody!)!;

    private string SentPrompt() => SentBody()["messages"]![0]!["content"]!.GetValue<string>();

    [Fact]
    public async Task TranslateAsync_ReturnsTheModelsAnswer()
    {
        _server.Answer("Получает имя.");

        (await TranslateAsync("Gets the name.")).Should().Be("Получает имя.");
    }

    [Fact]
    public async Task TranslateAsync_PostsToTheChatCompletionsEndpoint()
    {
        _server.Answer("Получает имя.");

        await TranslateAsync("Gets the name.");

        _server.Method.Should().Be(HttpMethod.Post);
        _server.Address.Should().Be(new Uri("http://127.0.0.1:8080/v1/chat/completions"));
        _server.Calls.Should().Be(1);
    }

    [Fact]
    public async Task TranslateAsync_SendsThePromptAsASingleUserMessage()
    {
        _server.Answer("Получает имя.");

        await TranslateAsync("Gets the name.");

        JsonArray messages = SentBody()["messages"]!.AsArray();
        messages.Should().HaveCount(1);
        messages[0]!["role"]!.GetValue<string>().Should().Be("user");
        SentPrompt().Should().Be(TranslationPrompt.Build(EnglishToRussian, "Gets the name."));
    }

    [Fact]
    public async Task TranslateAsync_AsksForTheSameAnswerEveryTime()
    {
        _server.Answer("Получает имя.");

        await TranslateAsync("Gets the name.");

        SentBody()["temperature"]!.GetValue<int>().Should().Be(0);
        SentBody()["cache_prompt"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task TranslateAsync_LimitsTheAnswerToTheLengthOfTheSource()
    {
        _server.Answer("Получает имя.");

        await TranslateAsync("Gets the name.");
        SentBody()["max_tokens"]!.GetValue<int>().Should().Be(96);

        await TranslateAsync(new string('a', 300));
        SentBody()["max_tokens"]!.GetValue<int>().Should().Be(300);
    }

    [Fact]
    public async Task TranslateAsync_SendsTokensAsTagsAndTurnsTheAnswersTagsBackIntoTokens()
    {
        _server.Answer("Используйте <g0>кэш</g0> вместе с <x1/>.");

        string result = await TranslateAsync("Use ⟦0:o⟧the cache⟦0:c⟧ with ⟦1⟧.");

        SentPrompt().Should().EndWith("Use <g0>the cache</g0> with <x1/>.");
        result.Should().Be("Используйте ⟦0:o⟧кэш⟦0:c⟧ вместе с ⟦1⟧.");
    }

    [Fact]
    public async Task TranslateAsync_TextThatAlreadyLooksLikeTags_GoesOutAndComesBackAsItIs()
    {
        _server.Answer("Запишите <x1/> в ⟦0⟧.");

        string result = await TranslateAsync("Write <x1/> to ⟦0⟧.");

        SentPrompt().Should().EndWith("Write <x1/> to ⟦0⟧.");
        result.Should().Be("Запишите <x1/> в ⟦0⟧.");
    }

    [Fact]
    public async Task TranslateAsync_TrimsTheAnswer()
    {
        _server.Answer("\n  Получает имя.  \n");

        (await TranslateAsync("Gets the name.")).Should().Be("Получает имя.");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, ProviderFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, ProviderFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.Unauthorized, ProviderFailureKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ProviderFailureKind.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderFailureKind.QuotaExceeded)]
    [InlineData(HttpStatusCode.BadRequest, ProviderFailureKind.InvalidResponse)]
    [InlineData(HttpStatusCode.NotFound, ProviderFailureKind.InvalidResponse)]
    public async Task TranslateAsync_ErrorStatus_ThrowsTheMatchingFailure(HttpStatusCode status, ProviderFailureKind expected)
    {
        _server.Status = status;
        _server.Body = "{}";

        Func<Task> act = () => TranslateAsync("Gets the name.");

        (await act.Should().ThrowAsync<TranslationProviderException>()).Which.Kind.Should().Be(expected);
    }

    [Fact]
    public async Task TranslateAsync_ServerNotReachable_ThrowsUnavailable()
    {
        _server.Failure = new HttpRequestException("Connection refused.");

        Func<Task> act = () => TranslateAsync("Gets the name.");

        (await act.Should().ThrowAsync<TranslationProviderException>())
            .Which.Kind.Should().Be(ProviderFailureKind.Unavailable);
    }

    [Fact]
    public async Task TranslateAsync_ServerTooSlow_ThrowsUnavailable()
    {
        // What HttpClient raises when its own timeout runs out: a cancellation the caller never asked for.
        _server.Failure = new TaskCanceledException("The request timed out.");

        Func<Task> act = () => TranslateAsync("Gets the name.");

        (await act.Should().ThrowAsync<TranslationProviderException>())
            .Which.Kind.Should().Be(ProviderFailureKind.Unavailable);
    }

    [Fact]
    public async Task TranslateAsync_CancelledByTheCaller_ThrowsOperationCanceled()
    {
        _server.Answer("Получает имя.");
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> act = () => _translator.TranslateAsync(EnglishToRussian, "Gets the name.", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _server.Calls.Should().Be(0);
    }

    [Fact]
    public async Task TranslateAsync_CancelledWhileTheServerIsWorking_ThrowsOperationCanceled()
    {
        using CancellationTokenSource cts = new();
        _server.BeforeAnswering = cts.Cancel;

        Func<Task> act = () => _translator.TranslateAsync(EnglishToRussian, "Gets the name.", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"message":{}}]}""")]
    [InlineData("""{"choices":[{"message":{"content":42}}]}""")]
    public async Task TranslateAsync_AnswerInAnUnexpectedForm_ThrowsInvalidResponse(string body)
    {
        _server.Body = body;

        Func<Task> act = () => TranslateAsync("Gets the name.");

        (await act.Should().ThrowAsync<TranslationProviderException>())
            .Which.Kind.Should().Be(ProviderFailureKind.InvalidResponse);
    }

    [Fact]
    public async Task TranslateAsync_AnswerCutOffAtTheLengthLimit_ThrowsInvalidResponse()
    {
        _server.Answer("Получает", finishReason: "length");

        Func<Task> act = () => TranslateAsync("Gets the name.");

        (await act.Should().ThrowAsync<TranslationProviderException>())
            .Which.Kind.Should().Be(ProviderFailureKind.InvalidResponse);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    public async Task TranslateAsync_EmptyAnswer_ThrowsInvalidResponse(string answer)
    {
        _server.Answer(answer);

        Func<Task> act = () => TranslateAsync("Gets the name.");

        (await act.Should().ThrowAsync<TranslationProviderException>())
            .Which.Kind.Should().Be(ProviderFailureKind.InvalidResponse);
    }

    [Fact]
    public async Task TranslateAsync_Failure_QuotesNeitherTheTextNorTheServersAnswer()
    {
        _server.Status = HttpStatusCode.BadRequest;
        _server.Body = """{"error":"cannot process: PRIVATE-COMMENT"}""";

        Func<Task> act = () => TranslateAsync("PRIVATE-COMMENT");

        (await act.Should().ThrowAsync<TranslationProviderException>())
            .Which.Message.Should().NotContain("PRIVATE-COMMENT");
    }

    // A stand-in for the model server: answers every request the same way and remembers the last one.
    private sealed class FakeServer : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string Body { get; set; } = string.Empty;

        public Exception? Failure { get; set; }

        /// <summary>Runs once the request has arrived, before it is answered.</summary>
        public Action? BeforeAnswering { get; set; }

        public HttpMethod? Method { get; private set; }

        public Uri? Address { get; private set; }

        public string? RequestBody { get; private set; }

        public int Calls { get; private set; }

        public void Answer(string content, string finishReason = "stop")
        {
            JsonObject choice = new()
            {
                ["finish_reason"] = finishReason,
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content },
            };

            Body = new JsonObject { ["choices"] = new JsonArray(choice) }.ToJsonString();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Method = request.Method;
            Address = request.RequestUri;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            BeforeAnswering?.Invoke();

            // A real server's connection is dropped when the request is cancelled.
            cancellationToken.ThrowIfCancellationRequested();

            if (Failure is not null)
            {
                throw Failure;
            }

            return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }
}