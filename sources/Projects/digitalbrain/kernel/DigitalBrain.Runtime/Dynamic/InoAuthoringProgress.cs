using DigitalBrain.Runtime.Neurons;
using Orleans;

namespace DigitalBrain.Runtime.Dynamic;

[GenerateSerializer]
public sealed record InoAuthoringProgress(
    [property: Id(0)] string Step, // Prompting, Compiling, Simulating, Gating, Transpiling, Activating
    [property: Id(1)] string SuggestedFqn,
    [property: Id(2)] int Attempt,
    [property: Id(3)] string? InoSource = null,
    [property: Id(4)] string? DiagnosticErrors = null
) : Synapse;
