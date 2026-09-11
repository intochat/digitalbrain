namespace DigitalBrain.Abstractions.Neurons;

// The RequestContext keys that carry lineage across a grain call: who called, which
// conversation, which signal caused it. Edges set them; the kernel reads them.
public static class NeuronRequestKeys
{
    public const string Caller = "db.caller";
    public const string Correlation = "db.correlation";
    public const string Causation = "db.causation";
}
