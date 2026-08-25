using System.Text.Json.Serialization;

namespace simple_bloomberg_terminal.Services.Llm;

public sealed record ChatRequest(
    string System,
    string Prompt,
    int MaxTokens = 4096,
    bool JsonObject = false,
    bool Fast = false);

public sealed record LlmMessage(string Role, string Content);

public sealed record OpenAiChatCompletionResponse(IReadOnlyList<OpenAiChatChoice>? Choices);

public sealed record OpenAiChatChoice(
    LlmMessage? Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

/// <summary>A non-streaming answer plus the provider's normalized reason for ending it.</summary>
public sealed record LlmCompletion(string Content, string? FinishReason);

/// <summary>One streamed reasoning or answer fragment surfaced to the chat.</summary>
public sealed record ChatDelta(string Kind, string Text);
