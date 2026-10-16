using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Sprocket.App.Ai;

/// <summary>One chat completion: the system and user prompts in, the model's text out.</summary>
internal interface IAiChatClient
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}

/// <summary>A provider failure the panel can show as-is — always PT-PT, never raw HTTP speak.</summary>
internal sealed class AiClientException(string message) : Exception(message);

internal static class AiClients
{
    /// <summary>Builds the client for a <see cref="SecretStore"/> provider id. The handler parameter exists
    /// for tests; production passes <see langword="null"/>.</summary>
    public static IAiChatClient Create(string provider, string apiKey, string model, HttpMessageHandler? handler = null) =>
        provider switch
        {
            SecretStore.Gemini => new GeminiChatClient(apiKey, model, handler),
            SecretStore.Glm => new GlmChatClient(apiKey, model, handler),
            _ => throw new ArgumentException($"fornecedor de IA desconhecido: {provider}"),
        };
}

/// <summary>
/// Google Gemini over the Generative Language REST API — the key a Google AI Pro subscriber gets from AI
/// Studio, with the plan's included quota. JSON-only output is requested at the protocol level
/// (<c>generationConfig.response_mime_type</c>) so the planner parses deterministically.
/// </summary>
internal sealed class GeminiChatClient(string apiKey, string model, HttpMessageHandler? handler = null) : IAiChatClient
{
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/";

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        string body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["system_instruction"] = new { parts = new[] { new { text = systemPrompt } } },
            ["contents"] = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
            ["generationConfig"] = new { response_mime_type = "application/json", temperature = 0.2 },
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}{model}:generateContent")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-goog-api-key", apiKey);
        return await AiClientHttp.SendAsync(request, handler, ReadGeminiTextAsync, cancellationToken);
    }

    private static async Task<string> ReadGeminiTextAsync(HttpResponseMessage response)
    {
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement candidate = doc.RootElement.TryGetProperty("candidates", out JsonElement candidates)
            && candidates.GetArrayLength() > 0
            ? candidates[0]
            : throw new AiClientException("O modelo não devolveu qualquer resposta — tente novamente.");
        string? reason = candidate.TryGetProperty("finishReason", out JsonElement finish) ? finish.GetString() : null;
        if (candidate.TryGetProperty("content", out JsonElement content)
            && content.TryGetProperty("parts", out JsonElement parts)
            && parts.GetArrayLength() > 0
            && parts[0].TryGetProperty("text", out JsonElement text))
            return text.GetString() ?? "";
        throw new AiClientException(
            $"O modelo devolveu uma resposta sem texto (motivo: {reason ?? "desconhecido"}) — reformule o pedido e tente novamente.");
    }
}

/// <summary>
/// GLM over Z.ai's Anthropic-compatible Messages API — the key from the user's GLM Coding Plan
/// subscription. Same request/response contract as the Anthropic API: <c>x-api-key</c> +
/// <c>anthropic-version</c> headers, <c>system</c>/<c>messages</c> body, text out of the first
/// <c>text</c> content block.
/// </summary>
internal sealed class GlmChatClient(string apiKey, string model, HttpMessageHandler? handler = null) : IAiChatClient
{
    private const string Endpoint = "https://api.z.ai/api/anthropic/v1/messages";

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        string body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = 8192,
            ["temperature"] = 0.2,
            ["system"] = systemPrompt,
            ["messages"] = new[] { new { role = "user", content = userPrompt } },
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        return await AiClientHttp.SendAsync(request, handler, ReadGlmTextAsync, cancellationToken);
    }

    private static async Task<string> ReadGlmTextAsync(HttpResponseMessage response)
    {
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (doc.RootElement.TryGetProperty("content", out JsonElement blocks) && blocks.GetArrayLength() > 0)
            foreach (JsonElement block in blocks.EnumerateArray())
                if (block.TryGetProperty("type", out JsonElement type) && type.GetString() == "text"
                    && block.TryGetProperty("text", out JsonElement text))
                    return text.GetString() ?? "";
        throw new AiClientException("O modelo devolveu uma resposta sem texto — reformule o pedido e tente novamente.");
    }
}

/// <summary>The shared POST/error pipeline: send, map failures to PT-PT, parse success with the provider's reader.</summary>
internal static class AiClientHttp
{
    internal static async Task<string> SendAsync(
        HttpRequestMessage request, HttpMessageHandler? handler,
        Func<HttpResponseMessage, Task<string>> readSuccess, CancellationToken cancellationToken)
    {
        using var http = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: true)
            { Timeout = TimeSpan.FromSeconds(120) };
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new AiClientException(await DescribeFailureAsync(response, cancellationToken));
            return await readSuccess(response);
        }
        catch (HttpRequestException ex)
        {
            throw new AiClientException($"Não foi possível contactar o serviço de IA: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiClientException("O serviço demorou demasiado a responder — tente novamente.");
        }
        catch (JsonException)
        {
            throw new AiClientException("O serviço devolveu uma resposta impossível de ler — tente novamente.");
        }
    }

    private static async Task<string> DescribeFailureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                return "A chave API foi rejeitada — verifique-a nas Preferências do Aven Editor.";
            case HttpStatusCode.TooManyRequests:
                return "A quota do serviço foi esgotada — aguarde um pouco e tente novamente.";
            case HttpStatusCode.RequestTimeout:
                return "O serviço demorou demasiado a responder — tente novamente.";
            case >= HttpStatusCode.InternalServerError:
                return $"O serviço de IA está temporariamente indisponível ({(int)response.StatusCode}) — tente novamente.";
            default:
                string snippet = await response.Content.ReadAsStringAsync(ct);
                if (snippet.Length > 200)
                    snippet = snippet[..200];
                return $"O pedido foi recusado ({(int)response.StatusCode}): {snippet}";
        }
    }
}
