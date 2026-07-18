using DigitalBrain.Runtime.Neurons;
using Orleans;

namespace DigitalBrain.SDK.DigitalBrain.Ai;

[GenerateSerializer]
public sealed record ParseDocRequest(
    [property: Id(0)] string DocumentName,
    [property: Id(1)] string TextContent) : Synapse;

[GenerateSerializer]
public sealed record ConceptsExtractedEvent(
    [property: Id(0)] string DocumentName,
    [property: Id(1)] string ConceptsJson,
    [property: Id(2)] string OverallSentiment) : Synapse;

[GenerateSerializer]
public sealed record CanvasRenderEvent(
    [property: Id(0)] string NodeId,
    [property: Id(1)] string Label,
    [property: Id(2)] double X,
    [property: Id(3)] double Y,
    [property: Id(4)] string Tone) : Synapse;
