using DigitalBrain.Core.Synapses;

namespace DigitalBrain.Abstractions.Ino;

[GenerateSerializer]
public sealed record ContextSnapshotUpdated([property: Id(0)] string SnapshotJson) : Synapse;

[GenerateSerializer]
public sealed record ContextChanged([property: Id(0)] string Key, [property: Id(1)] string Value) : Synapse;

[GenerateSerializer]
public sealed record ApprovalStillRequired([property: Id(0)] Guid ApprovalId, [property: Id(1)] string Description) : Synapse;

[GenerateSerializer]
public sealed record SessionNeedsUserInput([property: Id(0)] Guid SessionId, [property: Id(1)] string Prompt) : Synapse;

[GenerateSerializer]
public sealed record ContextCompacted([property: Id(0)] int OriginalSize, [property: Id(1)] int CompactedSize) : Synapse;

[GenerateSerializer]
public sealed record FileRead([property: Id(0)] string Path) : Synapse;

[GenerateSerializer]
public sealed record FileWritten([property: Id(0)] string Path, [property: Id(1)] string Content) : Synapse;

[GenerateSerializer]
public sealed record GitStatusObserved([property: Id(0)] string Branch, [property: Id(1)] bool IsDirty) : Synapse;

[GenerateSerializer]
public sealed record BuildStarted([property: Id(0)] string Project) : Synapse;

[GenerateSerializer]
public sealed record BuildCompleted([property: Id(0)] string Project, [property: Id(1)] bool Success) : Synapse;

[GenerateSerializer]
public sealed record ApprovalRequested(
    [property: Id(0)] Guid ApprovalId,
    [property: Id(1)] string ActionDescription,
    [property: Id(2)] string CapabilityName = "",
    [property: Id(3)] string SubjectId = "") : Synapse;

[GenerateSerializer]
public sealed record ApprovalCompleted(
    [property: Id(0)] Guid ApprovalId,
    [property: Id(1)] string CapabilityName) : Synapse;
