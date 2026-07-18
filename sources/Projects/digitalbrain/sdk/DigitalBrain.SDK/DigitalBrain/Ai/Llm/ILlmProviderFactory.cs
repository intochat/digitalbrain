using DigitalBrain.SDK.DigitalBrain.Ai.Models;
using Microsoft.Extensions.AI;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm;

public interface ILlmProviderFactory
{
    string ProviderName { get; }
    bool IsConfigured(IConfiguration config);
    IChatClient CreateClient(LlmModel model, IConfiguration config);
}
