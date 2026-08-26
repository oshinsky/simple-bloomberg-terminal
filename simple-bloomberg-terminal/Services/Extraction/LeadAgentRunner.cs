using System.Runtime.CompilerServices;

namespace simple_bloomberg_terminal.Services.Extraction;

public sealed class LeadAgentRunner(
    IChatLlm llm,
    ILogger<LeadAgentRunner> logger) : ILeadAgentRunner
{
    private const int MaxCompletionAttempts = 2;

    // Measurement needs one atomic ledger, so use a bounded completion and retry the whole request
    // when the provider times out or closes its response body prematurely. Interactive chat continues
    // to use StreamAsync below because partial text is useful there.
    public async Task<LlmCompletion> CompleteAsync(
        string systemPrompt, string filingContext, string userPrompt, int maxTokens,
        CancellationToken ct = default)
    {
        var request = new ChatRequest(
            systemPrompt + filingContext,
            userPrompt,
            MaxTokens: maxTokens);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await llm.CompleteAsync(request, ct);
            }
            catch (Exception ex) when (
                attempt < MaxCompletionAttempts &&
                !ct.IsCancellationRequested &&
                ex is HttpRequestException or IOException or TaskCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "Lead-agent completion transport failed on attempt {Attempt}/{MaxAttempts}; retrying",
                    attempt,
                    MaxCompletionAttempts);
            }
        }
    }

    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        string systemPrompt, string filingContext, IReadOnlyList<LlmMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new List<LlmMessage>
        {
            new("system", systemPrompt + filingContext)
        };
        request.AddRange(messages);

        await foreach (var delta in llm.StreamAsync(request, ct: ct))
            yield return delta;
    }
}
