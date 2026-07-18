using DigitalBrain.Core.Synapses;

namespace DigitalBrain.Abstractions.Ino;

[GenerateSerializer]
public sealed record InoSessionOptions(
    [property: Id(0)] string UserId,
    [property: Id(1)] string? WorkspacePath = null,
    [property: Id(2)] bool OpenVisibleConsole = true,
    [property: Id(3)] string? Purpose = null);

[GenerateSerializer]
public sealed record InoSessionInfo(
    [property: Id(0)] Guid SessionId,
    [property: Id(1)] string UserId,
    [property: Id(2)] string? WorkspacePath,
    [property: Id(3)] bool VisibleConsoleAttached,
    [property: Id(4)] DateTimeOffset StartedAt);

[GenerateSerializer]
public sealed record StartInoSession([property: Id(0)] InoSessionOptions Options) : Synapse;

[GenerateSerializer]
public sealed record InoSessionStarted([property: Id(0)] InoSessionInfo Session) : Synapse;

[GenerateSerializer]
public sealed record InoSessionNeedsInput(
    [property: Id(0)] Guid SessionId,
    [property: Id(1)] string Prompt,
    [property: Id(2)] string Reason) : Synapse;
