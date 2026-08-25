namespace simple_bloomberg_terminal.Tests;

public class ChatProvidersTests
{
    [Theory]
    [InlineData(ChatProviderId.DeepSeek, "deepseek-v4-pro", "deepseek-v4-flash")]
    [InlineData(ChatProviderId.Kimi, "kimi-k2.6", "kimi-k2.5")]
    [InlineData(ChatProviderId.OpenAi, "gpt-5.5", "gpt-5-mini")]
    [InlineData(ChatProviderId.Anthropic, "claude-opus-4-8", "claude-haiku-4-5")]
    public void Model_SelectsAutomaticStrongAndFastTiers(
        ChatProviderId provider, string strong, string fast)
    {
        Assert.Equal(strong, ChatProviders.Model(provider, fast: false));
        Assert.Equal(fast, ChatProviders.Model(provider, fast: true));
    }
}
