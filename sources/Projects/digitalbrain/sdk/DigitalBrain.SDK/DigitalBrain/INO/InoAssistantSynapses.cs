using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.INO;

[GenerateSerializer]
public sealed record InoChatRequest(string UserMessage
) : Synapse;

[GenerateSerializer]
public sealed record InoTaskStatus(
    string TaskId,
    string Description,
    string Status // Pending, Running, Completed, Failed
);

[GenerateSerializer]
public sealed record InoChatResponse(string AssistantReply,
    IReadOnlyList<InoTaskStatus>? ActiveTasks = null,
    string? RfwLibraryName = null,
    string? RfwRootWidget = null,
    string? RfwDataJson = null
) : Synapse;
