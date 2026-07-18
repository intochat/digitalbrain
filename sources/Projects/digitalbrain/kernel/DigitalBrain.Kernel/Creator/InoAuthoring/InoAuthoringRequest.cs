namespace DigitalBrain.Kernel.Creator.InoAuthoring;

// E-SDK #57 sub-issue B. Input to the InoLang authoring loop. The
// suggested FQN is a hint — the LLM may pick a different one in its
// `neuron <FQN>` line, and the loop honors what the document actually
// declares (the persisted manifest carries the document FQN, not this
// hint). Kept separate from the synapse contract layer so this stays
// usable as a pure in-process service ahead of a Creator-grain wiring.
public sealed record InoAuthoringRequest(
    string Intent,
    string SuggestedFqn,
    string LlmModelKey,
    int MaxAttempts = 5);
