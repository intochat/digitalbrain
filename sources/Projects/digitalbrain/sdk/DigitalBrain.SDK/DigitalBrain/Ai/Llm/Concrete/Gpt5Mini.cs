using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.SDK.DigitalBrain.Ai.Llm;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm.Concrete;

public interface IGpt5Mini : INeuron;

[ImplicitStreamSubscription(Gpt5MiniType)]
internal sealed class Gpt5Mini(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    [Llm<global::DigitalBrain.SDK.DigitalBrain.Ai.Models.Gpt5Mini>] IChatClient chat,
    ILogger<Gpt5Mini> logger)
    : LlmNeuronBase(incoming, outgoing, grains, chat, logger),
      IGpt5Mini,
      INeuronMetadata,
      IExternalNeuron
{
    public const string Gpt5MiniType = nameof(Gpt5Mini);

    public static NeuronId Id => new("ai/llm/openai/gpt-5-mini");
    public static string Icon => "openai";
    public static NeuronCapability Capabilities => NeuronCapability.Balanced | NeuronCapability.External;
}
