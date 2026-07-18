using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.SDK.DigitalBrain.Ai.Llm;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm.Concrete;

public interface ISonnet47 : INeuron;

[ImplicitStreamSubscription(Sonnet47Type)]
internal sealed class Sonnet47(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    [Llm<global::DigitalBrain.SDK.DigitalBrain.Ai.Models.Sonnet47>] IChatClient chat,
    ILogger<Sonnet47> logger)
    : LlmNeuronBase(incoming, outgoing, grains, chat, logger),
      ISonnet47,
      INeuronMetadata,
      IExternalNeuron
{
    public const string Sonnet47Type = nameof(Sonnet47);

    public static NeuronId Id => new("ai/llm/anthropic/claude-sonnet-4-7");
    public static string Icon => "anthropic";
    public static NeuronCapability Capabilities => NeuronCapability.Balanced | NeuronCapability.External;
}
