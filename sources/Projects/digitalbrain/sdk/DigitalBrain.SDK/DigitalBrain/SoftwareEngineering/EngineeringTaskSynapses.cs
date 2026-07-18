using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering;

[GenerateSerializer]
public sealed record EngineeringTaskRequest(
    [property: Id(1)] string TaskDescription,
    [property: Id(2)] string WorkspaceRoot,
    [property: Id(3)] List<string>? TargetFiles = null) : Synapse;

[GenerateSerializer]
public sealed record EngineeringTaskResponse(
    [property: Id(1)] bool Success,
    [property: Id(2)] List<string> ModifiedFiles,
    [property: Id(3)] string Feedback) : Synapse;
