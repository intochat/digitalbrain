using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.SDK.DigitalBrain.Ai.Llm;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm.Concrete;

public interface IOpus47 : INeuron;

[ImplicitStreamSubscription(Opus47Type)]
internal sealed class Opus47(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    [Llm<global::DigitalBrain.SDK.DigitalBrain.Ai.Models.Opus47>] IChatClient chat,
    ILogger<Opus47> logger)
    : LlmNeuronBase(incoming, outgoing, grains, chat, logger),
      IOpus47,
      INeuronMetadata,
      IExternalNeuron
{
    public const string Opus47Type = nameof(Opus47);

    public static NeuronId Id => new("ai/llm/anthropic/claude-opus-4-7");
    public static string Icon => "anthropic";
    public static NeuronCapability Capabilities => NeuronCapability.Reasoning | NeuronCapability.External;
}
