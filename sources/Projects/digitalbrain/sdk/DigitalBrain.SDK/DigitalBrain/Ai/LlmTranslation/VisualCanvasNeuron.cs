using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.SDK.DigitalBrain.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Ai.LlmTranslation;

[GrainType("DigitalBrain.SDK.Ai.VisualCanvasNeuron")]
[ImplicitStreamSubscription(VisualCanvasNeuronType)]
internal sealed class VisualCanvasNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<VisualCanvasNeuron> log)
    : Neuron(incoming, outgoing, grains, log),
      INeuronMetadata,
      IHandle<ConceptsExtractedEvent>
{
    public const string VisualCanvasNeuronType = nameof(VisualCanvasNeuron);

    public static NeuronId Id => new("ai/visual-canvas");
    public static string Icon => "canvas";
    public static NeuronCapability Capabilities => NeuronCapability.Balanced;

    private readonly Random _rand = new();

    public async Task HandleAsync(ConceptsExtractedEvent synapse, CancellationToken cancellationToken)
    {
        Logger.LogInformation("VisualCanvasNeuron received concepts for mapping: {ConceptsJson}", synapse.ConceptsJson);

        string[]? concepts = null;
        try
        {
            concepts = JsonSerializer.Deserialize<string[]>(synapse.ConceptsJson);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to deserialize concepts JSON inside VisualCanvasNeuron.");
        }

        concepts ??= new[] { "Concept" };

        var tone = synapse.OverallSentiment == "Critical" ? "red" : (synapse.OverallSentiment == "Warning" ? "amber" : "cyan");

        // We want to yield each concept mapped to visual 3D space
        for (int i = 0; i < concepts.Length; i++)
        {
            var concept = concepts[i];
            
            // Random floating coordinates inside comfortable visual canvas box
            var x = 120.0 + _rand.NextDouble() * 320.0;
            var y = 100.0 + _rand.NextDouble() * 200.0;
            var nodeId = $"concept_{Guid.NewGuid().ToString()[..6]}";

            var renderEvent = new CanvasRenderEvent(
                NodeId: nodeId,
                Label: concept,
                X: x,
                Y: y,
                Tone: tone
            );

            // Route broadcast synapse to GatewayNeuron so client watch receives it instantly
            var headers = SynapseMetadata.Create(
                synapseId: Guid.NewGuid(),
                correlationId: synapse.Headers.CorrelationId,
                causationId: synapse.Headers.SynapseId.Value,
                callerNeuronId: InstanceId,
                callerNeuronType: VisualCanvasNeuronType,
                receiverNeuronId: default,
                receiverNeuronType: "GatewayNeuron",
                timestamp: DateTimeOffset.UtcNow
            );

            await FireSynapseAsync(renderEvent with { Headers = headers }, cancellationToken);
            
            // Artificial tiny delay to let particles visually cascading nicely
            await Task.Delay(200, cancellationToken);
        }

        // Render an elegant Active UI card representing the completed document parsing
        var renderData = new System.Text.Json.Nodes.JsonObject
        {
            ["title"] = $"Document Parsed: {synapse.DocumentName}",
            ["body"] = $"Extracted {concepts.Length} concepts from the text. Visual mapping successfully projected onto the Operate Canvas. Sentiment: {synapse.OverallSentiment}.",
            ["initials"] = "D",
            ["tone"] = tone
        };

        await RenderAsync("digitalbrain", "sample_neuron", renderData, cancellationToken);
    }
}
