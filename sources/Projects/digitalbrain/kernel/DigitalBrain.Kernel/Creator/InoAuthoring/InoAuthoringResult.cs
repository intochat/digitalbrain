using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.Kernel.Creator.InoAuthoring;

// E-SDK #57 sub-issue B. Captures the loop's outcome for both the
// success case (the persisted .ino + its FQN + the manifest path) and
// the give-up case (final error + attempt count + last LLM draft for
// debugging). The brief's L6 gate requires the scenario to be green
// under InoScenarioProjection — `Green` is the L6 verdict, not just
// "the LLM produced something parseable".
public sealed record InoAuthoringResult(
    bool Green,
    int Attempts,
    string? AuthoredFqn,
    string? RelativeInoPath,
    string? LastInoSource,
    string? FinalError,
    InterpretedNeuronRegistration? Registration = null);
