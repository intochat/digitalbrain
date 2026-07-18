using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.SDK.DigitalBrain.Ai.Llm;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm.Concrete;

public interface IGpt5 : INeuron;

[ImplicitStreamSubscription(Gpt5Type)]
internal sealed class Gpt5(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    [Llm<global::DigitalBrain.SDK.DigitalBrain.Ai.Models.Gpt5>] IChatClient chat,
    ILogger<Gpt5> logger)
    : LlmNeuronBase(incoming, outgoing, grains, chat, logger),
      IGpt5,
      INeuronMetadata,
      IExternalNeuron
{
    public const string Gpt5Type = nameof(Gpt5);

    public static NeuronId Id => new("ai/llm/openai/gpt-5");
    public static string Icon => "openai";
    public static NeuronCapability Capabilities => NeuronCapability.Reasoning | NeuronCapability.External;
}
