using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace simple_bloomberg_terminal.Services.Llm;

/// <summary>
/// One configurable transport for providers that speak the OpenAI <c>/chat/completions</c> wire shape:
/// DeepSeek, Kimi (Moonshot), and OpenAI. Their base URL, provider id, and token-cap field
/// (<c>max_tokens</c> or <c>max_completion_tokens</c>) are supplied through DI. The body is built as
/// a dictionary so each provider emits exactly the keys its API accepts.
/// </summary>
public sealed class OpenAiCompatibleChatProvider : IChatProvider
{
    private readonly HttpClient _http;
    private readonly IUserApiKeyProvider _keys;
    private readonly string _maxTokensField;
    private readonly ILogger<OpenAiCompatibleChatProvider> _logger;

    public ChatProviderId Id { get; }

    public OpenAiCompatibleChatProvider(
        HttpClient http, IUserApiKeyProvider keys, ChatProviderId id,
        string maxTokensField, ILogger<OpenAiCompatibleChatProvider> logger)
    {
        _http = http;
        _keys = keys;
        Id = id;
        _maxTokensField = maxTokensField;
        _logger = logger;
    }

    private Task<string> KeyAsync(CancellationToken ct) =>
        _keys.RequireAsync(k => k.KeyFor(Id), () => MissingApiKeyException.ForParsingProvider(Id), ct);

    public async Task<LlmCompletion> CompleteAsync(
        string model, ChatRequest request, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new[]
            {
                new { role = "system", content = request.System },
                new { role = "user", content = request.Prompt }
            },
            [_maxTokensField] = request.MaxTokens
        };
        if (request.JsonObject) body["response_format"] = new { type = "json_object" };

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/chat/completions")
        {
            Content = JsonContent.Create(body),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", await KeyAsync(ct)) }
        };
        var resp = await _http.SendAsync(httpReq, ct);
        if (!resp.IsSuccessStatusCode)
            _logger.LogWarning("{Provider} complete {Model} failed: {Status}", Id, model, (int)resp.StatusCode);
        resp.EnsureSuccessStatusCode();
        var parsed = await resp.Content.ReadFromJsonAsync<OpenAiChatCompletionResponse>(ct);

        var choice = parsed?.Choices?.FirstOrDefault();
        return new LlmCompletion(choice?.Message?.Content ?? "", choice?.FinishReason);
    }

    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        string model, IReadOnlyList<LlmMessage> messages,
        int? maxTokens, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }),
            ["stream"] = true
        };
        if (maxTokens is { } cap) body[_maxTokensField] = cap;

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/chat/completions")
        {
            Content = JsonContent.Create(body),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", await KeyAsync(ct)) }
        };
        using var resp = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            _logger.LogWarning("{Provider} stream {Model} failed: {Status}", Id, model, (int)resp.StatusCode);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line["data:".Length..].Trim();
            if (data.Length == 0) continue;
            if (data == "[DONE]") break;

            JsonElement delta;
            try
            {
                using var doc = JsonDocument.Parse(data);
                delta = doc.RootElement.GetProperty("choices")[0].GetProperty("delta").Clone();
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException)
            {
                _logger.LogDebug(ex, "{Provider} SSE parse error on line: {Data}", Id, data);
                continue;
            }

            // reasoning_content is DeepSeek-specific; OpenAI-compatible providers that don't emit it
            // simply never hit this branch.
            if (delta.TryGetProperty("reasoning_content", out var r) &&
                r.ValueKind == JsonValueKind.String && r.GetString() is { Length: > 0 } rt)
                yield return new ChatDelta("reasoning", rt);

            if (delta.TryGetProperty("content", out var c) &&
                c.ValueKind == JsonValueKind.String && c.GetString() is { Length: > 0 } txt)
                yield return new ChatDelta("text", txt);
        }
    }
}
