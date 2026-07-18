using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.SDK.DigitalBrain.Ai.Llm;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm.Concrete;

public interface IClaude5Haiku : INeuron;

[ImplicitStreamSubscription(Claude5HaikuType)]
internal sealed class Claude5Haiku(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    [Llm<global::DigitalBrain.SDK.DigitalBrain.Ai.Models.Claude5Haiku>] IChatClient chat,
    ILogger<Claude5Haiku> logger)
    : LlmNeuronBase(incoming, outgoing, grains, chat, logger),
      IClaude5Haiku,
      INeuronMetadata,
      IExternalNeuron
{
    public const string Claude5HaikuType = nameof(Claude5Haiku);

    public static NeuronId Id => new("ai/llm/anthropic/claude-5-haiku");
    public static string Icon => "anthropic";
    public static NeuronCapability Capabilities => NeuronCapability.Fast | NeuronCapability.External;
}
