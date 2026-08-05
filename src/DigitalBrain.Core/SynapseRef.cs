namespace DigitalBrain;

public readonly record struct SynapseRef(NeuronId Source, long Sequence);
