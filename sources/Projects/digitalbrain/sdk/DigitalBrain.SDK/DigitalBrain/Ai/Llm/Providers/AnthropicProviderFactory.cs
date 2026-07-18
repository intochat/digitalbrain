using DigitalBrain.SDK.DigitalBrain.Ai.Llm;
using DigitalBrain.SDK.DigitalBrain.Ai.Models;
using Microsoft.Extensions.AI;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm.Providers;

public sealed class AnthropicProviderFactory : ILlmProviderFactory
{
    public string ProviderName => "anthropic";

    public bool IsConfigured(IConfiguration config)
        => !string.IsNullOrEmpty(config["DigitalBrain:Ai:AnthropicApiKey"]);

    public IChatClient CreateClient(LlmModel model, IConfiguration config)
        => throw new NotSupportedException(
            $"Anthropic provider is not yet wired (model '{model.Id}'). " +
            "Add an AnthropicChatClientAdapter implementation.");
}
