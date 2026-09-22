using System.ClientModel;
using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace DigitalBrain.AI;

internal sealed class OpenAIProviderFactory : OpenAICompatibleProviderFactory
{
    public override AiProvider Provider => AiProvider.OpenAI;

    protected override Uri? DefaultEndpoint => null;
}