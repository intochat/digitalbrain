using System.ClientModel;
using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace DigitalBrain.AI;

internal sealed class XAIProviderFactory : OpenAICompatibleProviderFactory
{
    public override AiProvider Provider => AiProvider.XAI;

    protected override Uri? DefaultEndpoint { get; } = new("https://api.x.ai/v1");
}