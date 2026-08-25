using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Integration tests for the shared OpenAI-compatible transport configured for DeepSeek against a
/// scripted HTTP handler. Covers what's fragile — the snake_case request shaping, the json_object
/// toggle, the choices→content envelope, and the streaming split of reasoning vs answer SSE frames.
/// </summary>
public class DeepSeekProviderTests
{
    private static OpenAiCompatibleChatProvider Build(
        ScriptedHttpHandler handler, UserApiKeys? keys = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.deepseek.com") },
            new FakeApiKeyProvider(keys ?? new UserApiKeys("ds-key", null, null)),
            ChatProviderId.DeepSeek,
            "max_tokens",
            NullLogger<OpenAiCompatibleChatProvider>.Instance);

    [Fact]
    public async Task CompleteAsync_JsonObject_SendsSnakeCaseRequest_AndReturnsContent()
    {
        var handler = ScriptedHttpHandler.Json(
            """{"choices":[{"message":{"role":"assistant","content":"{\"sources\":[]}"}}]}""");
        var client = Build(handler);

        var answer = await client.CompleteAsync(
            "deepseek-v4-flash", new ChatRequest("SYS", "USER", 1500, JsonObject: true),
            CancellationToken.None);

        Assert.Equal("{\"sources\":[]}", answer.Content);

        var req = handler.Single();
        Assert.Equal("Bearer ds-key", req.Authorization);
        Assert.Equal("/chat/completions", req.Uri!.AbsolutePath);

        using var doc = JsonDocument.Parse(req.Body);
        var root = doc.RootElement;
        Assert.Equal("deepseek-v4-flash", root.GetProperty("model").GetString());
        Assert.Equal(1500, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());

        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("SYS", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("USER", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_NoJsonObject_OmitsResponseFormat()
    {
        var handler = ScriptedHttpHandler.Json("""{"choices":[{"message":{"content":"plain"}}]}""");
        var client = Build(handler);

        var answer = await client.CompleteAsync(
            "deepseek-v4-flash", new ChatRequest("s", "u"), CancellationToken.None);

        Assert.Equal("plain", answer.Content);
        // response_format is optional; omitting it asks DeepSeek for its default free-form text.
        using var doc = JsonDocument.Parse(handler.Single().Body);
        Assert.False(doc.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task CompleteAsync_ReturnsFinishReason()
    {
        var handler = ScriptedHttpHandler.Json(
            """{"choices":[{"message":{"role":"assistant","content":"{\"sources\":["},"finish_reason":"length"}]}""");
        var client = Build(handler);

        var result = await client.CompleteAsync(
            "deepseek-v4-flash", new ChatRequest("s", "u", 16_000, JsonObject: true),
            CancellationToken.None);

        Assert.Equal("{\"sources\":[", result.Content);
        Assert.Equal("length", result.FinishReason);
    }

    [Fact]
    public async Task CompleteAsync_EmptyChoices_ReturnsEmptyString()
    {
        var handler = ScriptedHttpHandler.Json("""{"choices":[]}""");
        var client = Build(handler);

        var result = await client.CompleteAsync("m", new ChatRequest("s", "u"), CancellationToken.None);
        Assert.Equal("", result.Content);
    }

    [Fact]
    public async Task CompleteAsync_MissingKey_ThrowsAndMakesNoHttpCall()
    {
        var handler = ScriptedHttpHandler.Json("{}");
        var client = Build(handler, UserApiKeys.Empty);

        await Assert.ThrowsAsync<MissingApiKeyException>(() =>
            client.CompleteAsync("m", new ChatRequest("s", "u"), CancellationToken.None));
        Assert.Empty(handler.Captured);   // the key is required before the request is built
    }

    [Fact]
    public async Task CompleteAsync_ErrorStatus_ThrowsHttpRequestException()
    {
        var handler = new ScriptedHttpHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = Build(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CompleteAsync("m", new ChatRequest("s", "u"), CancellationToken.None));
    }

    [Fact]
    public async Task StreamAsync_SplitsReasoningAndText_InOrder_IgnoringRoleOnlyFrame()
    {
        // A role-only opening frame (no content), then a thinking-trace frame, then two answer frames,
        // then the terminator — exactly the v4 streaming shape the chat UI renders live.
        const string sse =
            "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"hmm\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\" world\"}}]}\n\n" +
            "data: [DONE]\n\n";
        var handler = ScriptedHttpHandler.Sse(sse);
        var client = Build(handler);

        var deltas = new List<ChatDelta>();
        await foreach (var d in client.StreamAsync(
            "deepseek-v4-pro", [new LlmMessage("user", "hi")], null, CancellationToken.None))
            deltas.Add(d);

        Assert.Collection(deltas,
            d => { Assert.Equal("reasoning", d.Kind); Assert.Equal("hmm", d.Text); },
            d => { Assert.Equal("text", d.Kind); Assert.Equal("Hello", d.Text); },
            d => { Assert.Equal("text", d.Kind); Assert.Equal(" world", d.Text); });

        using var doc = JsonDocument.Parse(handler.Single().Body);
        Assert.True(doc.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task StreamAsync_SkipsMalformedFrames()
    {
        // A garbage data line between two valid frames must not sink the stream (the parser swallows
        // JsonException per frame and keeps reading).
        const string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"A\"}}]}\n\n" +
            "data: {not json}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"B\"}}]}\n\n" +
            "data: [DONE]\n\n";
        var client = Build(ScriptedHttpHandler.Sse(sse));

        var text = "";
        await foreach (var d in client.StreamAsync(
            "m", [new LlmMessage("user", "hi")], null, CancellationToken.None))
            if (d.Kind == "text") text += d.Text;

        Assert.Equal("AB", text);
    }
}
