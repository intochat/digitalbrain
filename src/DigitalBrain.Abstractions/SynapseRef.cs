namespace DigitalBrain;

// Identity of a journaled fact: who said it, at which position of their journal.
// This is the dedup key, the causation reference and the answer reference — no GUIDs.
public readonly record struct SynapseRef(NeuronId Source, long Sequence);
