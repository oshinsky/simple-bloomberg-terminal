using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;

namespace simple_bloomberg_terminal.Tests;

public class LeadAgentRunnerTests
{
    [Fact]
    public async Task CompleteAsync_RetriesPrematureResponseAndKeepsBoundedRequest()
    {
        var llm = new FlakyLlm(failuresBeforeSuccess: 1);
        var runner = new LeadAgentRunner(llm, NullLogger<LeadAgentRunner>.Instance);

        var completion = await runner.CompleteAsync(
            "SYSTEM", "\nDIGEST", "USER", 16_000, CancellationToken.None);

        Assert.Equal("ledger", completion.Content);
        Assert.Equal(2, llm.CompletionCalls);
        Assert.NotNull(llm.LastRequest);
        Assert.Equal("SYSTEM\nDIGEST", llm.LastRequest!.System);
        Assert.Equal("USER", llm.LastRequest.Prompt);
        Assert.Equal(16_000, llm.LastRequest.MaxTokens);
        Assert.False(llm.LastRequest.JsonObject);
    }

    [Fact]
    public async Task CompleteAsync_DoesNotRetryCallerCancellation()
    {
        var llm = new FlakyLlm(failuresBeforeSuccess: int.MaxValue);
        var runner = new LeadAgentRunner(llm, NullLogger<LeadAgentRunner>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            runner.CompleteAsync("SYSTEM", "", "USER", 16_000, cts.Token));

        Assert.Equal(1, llm.CompletionCalls);
    }

    private sealed class FlakyLlm(int failuresBeforeSuccess) : IChatLlm
    {
        public int CompletionCalls { get; private set; }
        public ChatRequest? LastRequest { get; private set; }

        public Task<LlmCompletion> CompleteAsync(ChatRequest request, CancellationToken ct = default)
        {
            CompletionCalls++;
            LastRequest = request;
            if (CompletionCalls <= failuresBeforeSuccess)
            {
                if (ct.IsCancellationRequested)
                    throw new TaskCanceledException("Caller cancelled the request.");
                throw new IOException("The response ended prematurely. (ResponseEnded)");
            }
            return Task.FromResult(new LlmCompletion("ledger", "stop"));
        }

        public async IAsyncEnumerable<ChatDelta> StreamAsync(
            IReadOnlyList<LlmMessage> messages, int? maxTokens = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<(ChatProviderId Provider, string Model)> ResolveParsingAsync(
            CancellationToken ct = default) =>
            Task.FromResult((ChatProviderId.DeepSeek, "test-model"));
    }
}
