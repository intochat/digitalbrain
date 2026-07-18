using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.SDK.DigitalBrain.Ai;
using DigitalBrain.SDK.DigitalBrain.Ai.Models;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Embedding;

internal abstract class EmbeddingNeuronBase(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    IEmbeddingGenerator<string, Embedding<float>> generator,
    ILogger logger)
    : Neuron(incoming, outgoing, grains, logger),
      IHandle<EmbeddingRequest>
{
    protected IEmbeddingGenerator<string, Embedding<float>> Generator { get; } = generator;

    protected override async Task HandleSynapseAsync(Synapse synapse)
    {
        if (synapse is not EmbeddingRequest request) return;

        var generated = await Generator.GenerateAsync(request.Texts);
        var vectors = generated.Select(e => e.Vector.ToArray()).ToArray();
        var dimensions = vectors.Length > 0 ? vectors[0].Length : 0;
        var modelId = Generator.GetService(typeof(EmbeddingGeneratorMetadata)) is EmbeddingGeneratorMetadata metadata
            ? metadata.DefaultModelId ?? "unknown"
            : "unknown";

        await FireSynapseAsync(new EmbeddingResponse(Vectors: vectors,
        Dimensions: dimensions,
        ModelId: modelId) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: request.CorrelationId,
            causationId: request.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: request.CallerNeuronId,
            receiverNeuronType: request.CallerNeuronType ?? "External",
            timestamp: default
        ) });
    }
}

public interface ITextEmbedding3Small : INeuron;

[ImplicitStreamSubscription(TextEmbedding3SmallNeuronType)]
internal sealed class TextEmbedding3SmallNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    [Embedding<TextEmbedding3Small>]
        IEmbeddingGenerator<string, Embedding<float>> generator,
    ILogger<TextEmbedding3SmallNeuron> logger)
    : EmbeddingNeuronBase(incoming, outgoing, grains, generator, logger),
      ITextEmbedding3Small,
      INeuronMetadata,
      IEmbeddingNeuron,
      IExternalNeuron
{
    public const string TextEmbedding3SmallNeuronType = nameof(TextEmbedding3SmallNeuron);

    public static NeuronId Id => new("ai/embedding/openai/text-embedding-3-small");
    public static string Icon => "openai";
    public static NeuronCapability Capabilities => NeuronCapability.Embedding | NeuronCapability.External;
}
