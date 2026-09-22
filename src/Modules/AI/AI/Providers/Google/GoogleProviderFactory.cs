using System.ClientModel;
using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace DigitalBrain.AI;

internal sealed class GoogleProviderFactory : OpenAICompatibleProviderFactory
{
    public override AiProvider Provider => AiProvider.Google;

    protected override Uri? DefaultEndpoint { get; } =
        new("https://generativelanguage.googleapis.com/v1beta/openai/");
}