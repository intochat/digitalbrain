using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Visualization;

// Kernel → client tier hint. One per crossing-and-debounce per ClientId.
// Tier ∈ { "smooth", "strained", "red" }. Reason is a short human string
// like "p95 41ms over 1.2s" — surfaces in client logs only.
[GenerateSerializer]
public sealed record VisualLoadHint([property: Id(1)] string ClientId,
    [property: Id(2)] string Tier,
    [property: Id(3)] string Reason
) : Synapse;
