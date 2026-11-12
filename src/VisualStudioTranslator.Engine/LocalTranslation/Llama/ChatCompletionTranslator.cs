using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;

namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>
/// Translates one segment by asking a language model through the chat-completions API that llama.cpp's
/// server offers. It owns the request and the reading of the answer, and nothing else: the address and the
/// timeout belong to the <see cref="HttpClient"/> it is given, and starting a server or choosing a model is
/// someone else's job.
/// <para>
/// Whatever goes wrong comes out as a <see cref="TranslationProviderException"/> whose message says nothing
/// about the text. The server's answer is never quoted in one either, since a server may repeat the request
/// in it.
/// </para>
/// </summary>
internal sealed class ChatCompletionTranslator(HttpClient client)
{
    private static readonly Uri Endpoint = new("v1/chat/completions", UriKind.Relative);

    // A translation takes about as many tokens as its source has characters, at the very most. The floor
    // leaves room for a short segment; a model that has not stopped by the limit is not going to.
    private const int MinimumAnswerTokens = 96;

    /// <param name="text">The segment as the pipeline protects it: plain text with placeholder tokens.</param>
    /// <returns>The translation, with the same placeholder tokens if the model kept them.</returns>
    public async Task<string> TranslateAsync(LanguagePair pair, string text, CancellationToken cancellationToken)
    {
        // Checked here because not every HTTP handler looks at the token before it has an answer.
        cancellationToken.ThrowIfCancellationRequested();

        // Text that already holds something shaped like a tag goes out as it is. Its tokens are then at the
        // model's mercy, but that is rare, and the validator still catches a token that did not survive.
        bool tagged = MarkupTags.CanCarry(text);
        string wireText = tagged ? MarkupTags.ToTags(text) : text;

        string answer = await AskAsync(TranslationPrompt.Build(pair, wireText), wireText.Length, cancellationToken)
            .ConfigureAwait(false);

        return tagged ? MarkupTags.ToTokens(answer) : answer;
    }

    private async Task<string> AskAsync(string prompt, int sourceLength, CancellationToken cancellationToken)
    {
        JsonObject body = new()
        {
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt }),
            ["max_tokens"] = Math.Max(MinimumAnswerTokens, sourceLength),

            // The same text must always translate the same way, or the cache would hold a matter of luck.
            ["temperature"] = 0,

            // Reusing the server's state from the previous request saves time on the instruction, but the
            // server documents that it can change the answer slightly. The same answer every time matters more.
            ["cache_prompt"] = false,

            // A translation needs no reasoning written out first, and it would be cut off by the limit.
            ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },
        };

        int status;
        string responseBody;

        try
        {
            using StringContent content = new(body.ToJsonString(), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PostAsync(Endpoint, content, cancellationToken)
                .ConfigureAwait(false);

            status = (int)response.StatusCode;
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            // A cancellation the caller did not ask for is the client's own timeout.
            throw new TranslationProviderException(
                ProviderFailureKind.Unavailable, "The translation server could not be reached or did not answer in time.", exception);
        }

        if (status is < 200 or > 299)
        {
            throw Failure(status);
        }

        return ReadAnswer(responseBody);
    }

    private static TranslationProviderException Failure(int status)
    {
        string code = status.ToString(CultureInfo.InvariantCulture);

        return status switch
        {
            401 or 403 => new TranslationProviderException(
                ProviderFailureKind.Unauthorized, $"The translation server rejected the credentials (HTTP {code})."),
            429 => new TranslationProviderException(
                ProviderFailureKind.QuotaExceeded, $"The translation server is refusing requests for now (HTTP {code})."),

            // Includes 503, which llama.cpp's server answers while it is still loading the model.
            >= 500 => new TranslationProviderException(
                ProviderFailureKind.Unavailable, $"The translation server is not ready (HTTP {code})."),
            _ => new TranslationProviderException(
                ProviderFailureKind.InvalidResponse, $"The translation server refused the request (HTTP {code})."),
        };
    }

    private static string ReadAnswer(string responseBody)
    {
        string? answer;
        bool cutShort;

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement choice = document.RootElement.GetProperty("choices")[0];

            cutShort = choice.TryGetProperty("finish_reason", out JsonElement reason)
                && reason.ValueKind == JsonValueKind.String
                && reason.GetString() == "length";
            answer = choice.GetProperty("message").GetProperty("content").GetString();
        }
        catch (Exception exception) when (exception
            is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new TranslationProviderException(
                ProviderFailureKind.InvalidResponse, "The translation server's answer was not in the expected form.", exception);
        }

        if (cutShort)
        {
            // Half a translation is worse than none: it reads as complete.
            throw new TranslationProviderException(
                ProviderFailureKind.InvalidResponse, "The model did not finish its answer within the length limit.");
        }

        answer = answer?.Trim();

        if (string.IsNullOrEmpty(answer))
        {
            throw new TranslationProviderException(ProviderFailureKind.InvalidResponse, "The model returned an empty answer.");
        }

        return answer;
    }
}