namespace simple_bloomberg_terminal.Services.Extraction;

// Executes a lead-agent request without imposing chat, handoff, save-block, or measurement semantics.
public interface ILeadAgentRunner
{
    Task<LlmCompletion> CompleteAsync(
        string systemPrompt, string filingContext, string userPrompt, int maxTokens,
        CancellationToken ct = default);

    IAsyncEnumerable<ChatDelta> StreamAsync(
        string systemPrompt, string filingContext, IReadOnlyList<LlmMessage> messages,
        CancellationToken ct = default);
}
