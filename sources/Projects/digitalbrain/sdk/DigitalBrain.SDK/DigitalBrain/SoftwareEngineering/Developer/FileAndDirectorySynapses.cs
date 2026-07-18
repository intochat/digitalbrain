using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

// --- File operations ---

[GenerateSerializer]
public sealed record ReadFileRequest : Synapse;

[GenerateSerializer]
public sealed record FileContentResponse([property: Id(1)] bool Success,
    [property: Id(2)] string Content,
    [property: Id(3)] string? ErrorMessage = null
) : Synapse;

[GenerateSerializer]
public sealed record ApplyCodeEditRequest([property: Id(1)] string NewContent,
    [property: Id(2)] string? CommitMessage = null
) : Synapse;

[GenerateSerializer]
public sealed record ApplyCodeEditResponse([property: Id(1)] bool Success,
    [property: Id(2)] string? ErrorMessage = null
) : Synapse;

// --- Directory operations ---

[GenerateSerializer]
public sealed record ReadDirectoryRequest : Synapse;

[GenerateSerializer]
public sealed record DirectoryContentsResponse([property: Id(1)] bool Success,
    [property: Id(2)] IReadOnlyList<string> Files,
    [property: Id(3)] IReadOnlyList<string> Directories,
    [property: Id(4)] string? ErrorMessage = null
) : Synapse;
