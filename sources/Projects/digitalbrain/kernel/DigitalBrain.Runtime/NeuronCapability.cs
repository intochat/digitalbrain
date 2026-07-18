namespace DigitalBrain.Runtime;

[Flags]
public enum NeuronCapability
{
    None = 0,
    Fast = 1 << 0,
    Balanced = 1 << 1,
    Reasoning = 1 << 2,
    Voice = 1 << 3,
    Embedding = 1 << 4,
    Storage = 1 << 5,
    External = 1 << 6,
    Generated = 1 << 7,
}
