using System.Net;
using System.Text;
using System.Text.Json;
using Sprocket.App.Ai;
using Xunit;

namespace Sprocket.App.Tests;

/// <summary>
/// The assistant's two REST clients (Etapa D), driven entirely through a fake handler — zero network.
/// Asserts each provider's wire contract (URL, auth headers, body) and the PT-PT failure mapping the
/// panel shows verbatim.
/// </summary>
public sealed class AiClientsTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string GeminiSuccess(string text) => $$"""
        {"candidates":[{"content":{"parts":[{"text":{{JsonSerializer.Serialize(text)}}}]},"finishReason":"STOP"}]}
        """;

    private static string GlmSuccess(string text) =>
        $$"""{"content":[{"type":"text","text":{{JsonSerializer.Serialize(text)}}}]}""";

    [Fact]
    public async Task Gemini_Sends_The_Expected_Request()
    {
        var handler = new FakeHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, GeminiSuccess("olá"))));
        IAiChatClient client = AiClients.Create(SecretStore.Gemini, "k-g", "gemini-2.5-flash", handler);

        string text = await client.CompleteAsync("sistema", "pedido do utilizador");

        Assert.Equal("olá", text);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent",
            handler.Request.RequestUri!.ToString());
        Assert.Equal("k-g", handler.Request.Headers.GetValues("x-goog-api-key").Single());
        using JsonDocument body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("sistema", body.RootElement.GetProperty("system_instruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("pedido do utilizador", body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("application/json", body.RootElement.GetProperty("generationConfig").GetProperty("response_mime_type").GetString());
    }

    [Fact]
    public async Task Glm_Sends_The_Expected_Request()
    {
        var handler = new FakeHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, GlmSuccess("plano"))));
        IAiChatClient client = AiClients.Create(SecretStore.Glm, "k-z", "glm-4.6", handler);

        string text = await client.CompleteAsync("sistema", "pedido");

        Assert.Equal("plano", text);
        Assert.Equal("https://api.z.ai/api/anthropic/v1/messages", handler.Request!.RequestUri!.ToString());
        Assert.Equal("k-z", handler.Request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", handler.Request.Headers.GetValues("anthropic-version").Single());
        using JsonDocument body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("glm-4.6", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("sistema", body.RootElement.GetProperty("system").GetString());
        Assert.Equal("pedido", body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal(8192, body.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Theory]
    [InlineData(SecretStore.Gemini)]
    [InlineData(SecretStore.Glm)]
    public async Task Rejected_Key_And_Spent_Quota_Map_To_PT_Messages(string provider)
    {
        var handler = new FakeHandler(_ => Task.FromResult(Json(HttpStatusCode.Unauthorized, "{}")));
        IAiChatClient client = AiClients.Create(provider, "k", "m", handler);
        AiClientException ex = await Assert.ThrowsAsync<AiClientException>(() => client.CompleteAsync("s", "u"));
        Assert.Contains("chave API foi rejeitada", ex.Message);

        var quota = new FakeHandler(_ => Task.FromResult(Json(HttpStatusCode.TooManyRequests, "{}")));
        client = AiClients.Create(provider, "k", "m", quota);
        ex = await Assert.ThrowsAsync<AiClientException>(() => client.CompleteAsync("s", "u"));
        Assert.Contains("quota", ex.Message);
    }

    [Fact]
    public async Task Transport_And_Timeout_Failures_Map_To_PT_Messages()
    {
        var broken = new FakeHandler(_ => Task.FromResult(Json(HttpStatusCode.InternalServerError, "{}")));
        IAiChatClient client = AiClients.Create(SecretStore.Gemini, "k", "m", broken);
        AiClientException ex = await Assert.ThrowsAsync<AiClientException>(() => client.CompleteAsync("s", "u"));
        Assert.Contains("temporariamente indisponível", ex.Message);

        var hanging = new FakeHandler(_ => throw new HttpRequestException("sem ligação"));
        client = AiClients.Create(SecretStore.Glm, "k", "m", hanging);
        ex = await Assert.ThrowsAsync<AiClientException>(() => client.CompleteAsync("s", "u"));
        Assert.Contains("Não foi possível contactar o serviço de IA", ex.Message);

        var slow = new FakeHandler(_ => throw new TaskCanceledException());
        client = AiClients.Create(SecretStore.Gemini, "k", "m", slow);
        ex = await Assert.ThrowsAsync<AiClientException>(() => client.CompleteAsync("s", "u"));
        Assert.Contains("demorou demasiado", ex.Message);
    }

    [Fact]
    public async Task Responses_Without_Text_Map_To_PT_Messages()
    {
        var blocked = new FakeHandler(_ => Task.FromResult(Json(HttpStatusCode.OK,
            """{"candidates":[{"finishReason":"SAFETY"}]}""")));
        IAiChatClient client = AiClients.Create(SecretStore.Gemini, "k", "m", blocked);
        AiClientException ex = await Assert.ThrowsAsync<AiClientException>(() => client.CompleteAsync("s", "u"));
        Assert.Contains("sem texto", ex.Message);

        var empty = new FakeHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, """{"content":[]}""")));
        client = AiClients.Create(SecretStore.Glm, "k", "m", empty);
        ex = await Assert.ThrowsAsync<AiClientException>(() => client.CompleteAsync("s", "u"));
        Assert.Contains("sem texto", ex.Message);
    }
}
