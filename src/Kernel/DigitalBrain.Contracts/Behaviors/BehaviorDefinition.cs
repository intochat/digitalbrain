using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Behaviors;

[GenerateSerializer, Alias("db.v2.payload-contract")]
public sealed record PayloadContract([property: Id(0)] string SignalType, [property: Id(1)] string Schema);

[GenerateSerializer, Alias("db.v2.behavior-node")]
public sealed record BehaviorNodeDefinition(
    [property: Id(0)] string Role,
    [property: Id(1)] string Capability,
    [property: Id(2)] string Configuration,
    [property: Id(3)] PayloadContract? Input = null,
    [property: Id(4)] PayloadContract? Output = null,
    [property: Id(5)] NeuronId? SharedNeuron = null);

[GenerateSerializer, Alias("db.v2.behavior-connection")]
public sealed record BehaviorConnection([property: Id(0)] string From, [property: Id(1)] string To);

[GenerateSerializer, Alias("db.v2.behavior-definition")]
public sealed record BehaviorDefinition(
    [property: Id(0)] string Name,
    [property: Id(1)] IReadOnlyList<BehaviorNodeDefinition> Nodes,
    [property: Id(2)] IReadOnlyList<BehaviorConnection> Connections,
    [property: Id(3)] BehaviorAuthorization? Authorization = null);

[GenerateSerializer, Alias("db.v2.behavior-authorization")]
public sealed record BehaviorAuthorization([property: Id(0)] string WorkspaceId, [property: Id(1)] string GrantId);

public enum BehaviorOwnership { Shared, Owned }
public enum BehaviorStatus { Draft, Starting, Running, Stopping, Stopped, Faulted }

[GenerateSerializer, Alias("db.v2.behavior-capability")]
public sealed record BehaviorCapability(
    [property: Id(0)] string Id, [property: Id(1)] string GrainType,
    [property: Id(2)] BehaviorOwnership Ownership, [property: Id(3)] string Description,
    [property: Id(4)] IReadOnlyList<BehaviorSourceContract>? Sources = null);

[GenerateSerializer, Alias("db.v2.behavior-source-contract")]
public sealed record BehaviorSourceContract([property: Id(0)] string GrainType,
    [property: Id(1)] IReadOnlyList<PayloadContract> Outputs);

[GenerateSerializer, Alias("db.v2.behavior-validation")]
public sealed record BehaviorValidation([property: Id(0)] bool Valid, [property: Id(1)] IReadOnlyList<string> Errors);

[GenerateSerializer, Alias("db.v2.behavior-version")]
public sealed record BehaviorVersion([property: Id(0)] long Value);

[GenerateSerializer, Alias("db.v2.behavior-node-binding")]
public sealed record BehaviorNodeBinding([property: Id(0)] string Role, [property: Id(1)] NeuronId Neuron, [property: Id(2)] bool Owned);

[GenerateSerializer, Alias("db.v2.behavior-snapshot")]
public sealed record BehaviorSnapshot(
    [property: Id(0)] BehaviorDefinition? Definition,
    [property: Id(1)] long Version,
    [property: Id(2)] BehaviorStatus Status,
    [property: Id(3)] string? RunId,
    [property: Id(4)] IReadOnlyList<BehaviorNodeBinding> Bindings,
    [property: Id(5)] string? Error = null);

[GenerateSerializer, Alias("db.v2.save-behavior")]
public sealed record SaveBehavior(
    [property: Id(2)] BehaviorDefinition Definition, CommandId Id, long? ExpectedVersion = null) : Command(Id, ExpectedVersion);

[GenerateSerializer, Alias("db.v2.change-behavior")]
public sealed record ChangeBehavior(CommandId Id, long? ExpectedVersion = null) : Command(Id, ExpectedVersion);

[GenerateSerializer, Alias("db.v2.behavior-node-activation")]
public sealed record BehaviorNodeActivation(
    [property: Id(0)] string Owner,
    [property: Id(1)] BehaviorNodeDefinition Definition,
    [property: Id(2)] bool Enabled,
    [property: Id(3)] BehaviorAuthorization? Authorization = null,
    [property: Id(4)] string? BehaviorId = null);

[GenerateSerializer, Alias("db.v2.behavior-node-status")]
public sealed record BehaviorNodeStatus(
    [property: Id(0)] bool Enabled,
    [property: Id(1)] string? Error,
    [property: Id(2)] string? UncertainAction);

[GenerateSerializer, Alias("db.v2.behavior-diagnostic")]
public sealed record BehaviorDiagnostic([property: Id(0)] string Role,
    [property: Id(1)] NeuronId Neuron, [property: Id(2)] BehaviorNodeStatus Status);
