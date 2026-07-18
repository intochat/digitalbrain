namespace Ino.Core.Hosting.Llm;

/// <summary>
/// Centralised configuration keys for the LLM stack. The AppHost surfaces
/// each declared provider's API key as an Aspire secret parameter; the
/// dashboard prompts on first run and forwards the value into the silo's
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> under
/// <c>Ino:Llm:ApiKeys:&lt;provider&gt;</c>. Per-provider helpers below give
/// each <see cref="ILlmProviderFactory"/> a stable lookup key. Mirrors
/// IAW's <c>Core/AI/LlmConfig.cs</c>.
/// </summary>
public static class LlmConfig
{
    public const string ApiKeyPrefix = "Ino:Llm:ApiKeys:";

    public static string ApiKey(string provider) => ApiKeyPrefix + provider.ToLowerInvariant();

    // Convenience constants for the providers ino currently knows about.
    // Adding a new provider = add a constant here + one ILlmProviderFactory
    // implementation in Ino.Llm.<Provider>.
    public const string XaiApiKey = ApiKeyPrefix + "xai";
    public const string OpenAiApiKey = ApiKeyPrefix + "openai";
    public const string AnthropicApiKey = ApiKeyPrefix + "anthropic";
}
