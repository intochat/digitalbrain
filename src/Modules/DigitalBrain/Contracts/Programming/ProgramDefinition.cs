using System.Text.Json;

namespace DigitalBrain.Abstractions.Programming;

[GenerateSerializer, Alias("db.program.definition")]
public sealed record ProgramDefinition(
    [property: Id(0)] string Id,
    [property: Id(1)] string Name,
    [property: Id(2)] string Trigger,
    [property: Id(3)] IReadOnlyList<ProgramNode> Nodes,
    [property: Id(4)] IReadOnlyList<ProgramSynapse> Synapses,
    [property: Id(5)] string? Description = null);

[GenerateSerializer, Alias("db.program.node")]
public sealed record ProgramNode(
    [property: Id(0)] string Id,
    [property: Id(1)] string Kind,
    [property: Id(2)] JsonElement Config,
    [property: Id(3)] string InputType = "Json",
    [property: Id(4)] string OutputType = "Json");

[GenerateSerializer, Alias("db.program.synapse")]
public sealed record ProgramSynapse([property: Id(0)] string From, [property: Id(1)] string To);

[GenerateSerializer, Alias("db.program.validation")]
public sealed record ProgramValidation(
    [property: Id(0)] bool Valid,
    [property: Id(1)] IReadOnlyList<string> Errors,
    [property: Id(2)] IReadOnlyList<string> Order);

[GenerateSerializer, Alias("db.program.revision")]
public sealed record ProgramRevision(
    [property: Id(0)] long Version,
    [property: Id(1)] ProgramDefinition Definition,
    [property: Id(2)] DateTimeOffset CreatedAt);

[GenerateSerializer, Alias("db.program.receipt")]
public sealed record ProgramReceipt([property: Id(0)] string OperationId, [property: Id(1)] string? Error);

[GenerateSerializer, Alias("db.program.snapshot")]
public sealed record ProgramSnapshot(
    [property: Id(0)] string Id,
    [property: Id(1)] long Version,
    [property: Id(2)] bool Enabled,
    [property: Id(3)] ProgramDefinition? Definition,
    [property: Id(4)] IReadOnlyList<ProgramRevision> Versions,
    [property: Id(5)] IReadOnlyList<string> Runs,
    [property: Id(6)] IReadOnlyList<ProgramReceipt> Receipts);

[GenerateSerializer, Alias("db.program.step")]
public sealed record ProgramStep(
    [property: Id(0)] string NodeId,
    [property: Id(1)] string Kind,
    [property: Id(2)] string Status,
    [property: Id(3)] JsonElement Input,
    [property: Id(4)] JsonElement Output,
    [property: Id(5)] string? Error,
    [property: Id(6)] DateTimeOffset StartedAt,
    [property: Id(7)] DateTimeOffset CompletedAt);

[GenerateSerializer, Alias("db.program.run")]
public sealed record ProgramRunSnapshot(
    [property: Id(0)] string RunId,
    [property: Id(1)] string ProgramId,
    [property: Id(2)] long Version,
    [property: Id(3)] string Status,
    [property: Id(4)] JsonElement Input,
    [property: Id(5)] JsonElement Output,
    [property: Id(6)] IReadOnlyList<ProgramStep> Steps,
    [property: Id(7)] string? Error,
    [property: Id(8)] DateTimeOffset StartedAt,
    [property: Id(9)] DateTimeOffset? CompletedAt);
