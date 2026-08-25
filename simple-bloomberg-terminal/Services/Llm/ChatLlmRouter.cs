using System.Runtime.CompilerServices;

namespace simple_bloomberg_terminal.Services.Llm;

/// <summary>
/// The parsing &amp; structuring LLM, as the rest of the app sees it. Callers no longer pass a model or
/// know a provider or model — they hand over a request and get a completion/stream back. The signed-in
/// user chooses the provider; the router automatically selects its fast or strong model per request.
/// </summary>
public interface IChatLlm
{
    Task<LlmCompletion> CompleteAsync(ChatRequest request, CancellationToken ct = default);

    IAsyncEnumerable<ChatDelta> StreamAsync(
        IReadOnlyList<LlmMessage> messages, int? maxTokens = null, CancellationToken ct = default);

    /// <summary>The provider+model this request will use — for labelling stored output (e.g. ReviewerModel).</summary>
    Task<(ChatProviderId Provider, string Model)> ResolveParsingAsync(CancellationToken ct = default);
}

/// <inheritdoc cref="IChatLlm"/>
public sealed class ChatLlmRouter : IChatLlm
{
    private readonly IUserApiKeyProvider _keys;
    private readonly IReadOnlyDictionary<ChatProviderId, IChatProvider> _providers;

    public ChatLlmRouter(IUserApiKeyProvider keys, IEnumerable<IChatProvider> providers)
    {
        _keys = keys;
        // Last registration wins per id; in practice each provider id is registered once.
        _providers = providers.ToDictionary(p => p.Id);
    }

    public async Task<(ChatProviderId Provider, string Model)> ResolveParsingAsync(CancellationToken ct = default)
    {
        var keys = await _keys.GetAsync(ct);
        return (keys.ParsingProvider, ChatProviders.StrongModel(keys.ParsingProvider));
    }

    public async Task<LlmCompletion> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        var keys = await _keys.GetAsync(ct);
        var model = ChatProviders.Model(keys.ParsingProvider, request.Fast);
        return await Provider(keys.ParsingProvider).CompleteAsync(model, request, ct);
    }

    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        IReadOnlyList<LlmMessage> messages, int? maxTokens = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (id, model) = await ResolveParsingAsync(ct);
        await foreach (var d in Provider(id).StreamAsync(model, messages, maxTokens, ct))
            yield return d;
    }

    // The chosen provider, or a clear failure if it somehow wasn't registered.
    private IChatProvider Provider(ChatProviderId id) =>
        _providers.TryGetValue(id, out var p) ? p
            : throw new InvalidOperationException($"No chat provider registered for {id}.");
}
