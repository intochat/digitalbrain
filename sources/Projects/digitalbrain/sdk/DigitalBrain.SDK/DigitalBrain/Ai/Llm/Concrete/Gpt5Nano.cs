using Microsoft.Extensions.AI;
using Orleans.Journaling;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime;
using DigitalBrain.SDK.DigitalBrain.Ai.Llm;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm.Concrete;

public interface IGpt5Nano : INeuron;

[ImplicitStreamSubscription(Gpt5NanoType)]
internal sealed class Gpt5Nano(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    [Llm<global::DigitalBrain.SDK.DigitalBrain.Ai.Models.Gpt5Nano>] IChatClient chat,
    ILogger<Gpt5Nano> logger)
    : LlmNeuronBase(incoming, outgoing, grains, chat, logger),
      IGpt5Nano,
      INeuronMetadata,
      IExternalNeuron
{
    public const string Gpt5NanoType = nameof(Gpt5Nano);

    public static NeuronId Id => new("ai/llm/openai/gpt-5-nano");
    public static string Icon => "openai";
    public static NeuronCapability Capabilities => NeuronCapability.Fast | NeuronCapability.External;
}
