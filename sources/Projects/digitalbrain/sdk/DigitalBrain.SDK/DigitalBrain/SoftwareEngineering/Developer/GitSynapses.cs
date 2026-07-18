using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

[GenerateSerializer]
public sealed record GitCommitRequest([property: Id(1)] string Message,
    [property: Id(2)] IReadOnlyList<string>? Files = null,
    [property: Id(3)] bool AutoStage = true
) : Synapse;

[GenerateSerializer]
public sealed record SubmitPullRequest([property: Id(1)] string Title,
    [property: Id(2)] string Body,
    [property: Id(3)] string SourceBranch,
    [property: Id(4)] string TargetBranch = "master",
    [property: Id(5)] bool Draft = false
) : Synapse;

[GenerateSerializer]
public sealed record GitStatusRequest : Synapse;

[GenerateSerializer]
public sealed record GitStatusResponse([property: Id(1)] bool Success,
    [property: Id(2)] string CurrentBranch,
    [property: Id(3)] IReadOnlyList<string> ChangedFiles,
    [property: Id(4)] string? ErrorMessage = null
) : Synapse;
