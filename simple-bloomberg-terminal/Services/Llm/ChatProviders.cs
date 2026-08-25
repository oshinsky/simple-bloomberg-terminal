namespace simple_bloomberg_terminal.Services.Llm;

/// <summary>
/// The chat/parsing LLM providers the app can route the "parsing &amp; structuring" role to. The
/// web-search role is always Perplexity (its sonar models do search + answer in one call, so it
/// can't be swapped for a plain chat provider) and is therefore not in this enum.
/// </summary>
public enum ChatProviderId
{
    DeepSeek,
    Kimi,
    OpenAi,
    Anthropic
}

/// <summary>
/// Static catalog shared by the provider picker and router. Each provider declares exactly one strong
/// and one fast model; callers select a tier, never a concrete model name.
/// </summary>
public static class ChatProviders
{
    /// <summary>One parsing provider: display/key metadata plus the strong and fast models selected
    /// automatically for lead and worker calls.</summary>
    public record ProviderInfo(
        ChatProviderId Id, string Display, string KeyLabel, string KeyHelpUrl,
        string StrongModel, string FastModel);

    // Models current as of 2026-06. Most capable / default first; FastModel is the cheap/quick tier
    // used for the parallel filing scan (triage + per-chunk workers).
    public static readonly IReadOnlyList<ProviderInfo> Parsing =
    [
        new(ChatProviderId.DeepSeek, "DeepSeek", "DeepSeek", "https://platform.deepseek.com/api_keys",
            "deepseek-v4-pro", "deepseek-v4-flash"),
        new(ChatProviderId.Kimi, "Kimi (Moonshot)", "Kimi", "https://platform.moonshot.ai/console/api-keys",
            "kimi-k2.6", "kimi-k2.5"),
        new(ChatProviderId.OpenAi, "OpenAI", "OpenAI", "https://platform.openai.com/api-keys",
            "gpt-5.5", "gpt-5-mini"),
        new(ChatProviderId.Anthropic, "Anthropic", "Anthropic", "https://console.anthropic.com/settings/keys",
            "claude-opus-4-8", "claude-haiku-4-5"),
    ];

    /// <summary>Perplexity sonar variants for the web-search role (provider is always Perplexity).</summary>
    public static readonly IReadOnlyList<string> WebSearchModels =
        ["sonar-pro", "sonar", "sonar-reasoning-pro", "sonar-deep-research"];

    public const string DefaultWebSearchModel = "sonar-pro";

    public static ProviderInfo Info(ChatProviderId id) => Parsing.First(p => p.Id == id);

    /// <summary>The provider's fast/cheap model — used for the high-volume parallel filing scan (triage
    /// + per-chunk workers), where the heavyweight strong model is slow and costly. Interactive chat
    /// and other non-fast calls use the provider's strong tier.</summary>
    public static string StrongModel(ChatProviderId id) => Info(id).StrongModel;

    public static string FastModel(ChatProviderId id) => Info(id).FastModel;

    public static string Model(ChatProviderId id, bool fast) =>
        fast ? FastModel(id) : StrongModel(id);

    /// <summary>Parse a stored provider name back to the enum, defaulting to DeepSeek (the app's original).</summary>
    public static ChatProviderId ParseProvider(string? stored) =>
        Enum.TryParse<ChatProviderId>(stored, out var id) ? id : ChatProviderId.DeepSeek;
}
