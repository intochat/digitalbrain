namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.register-instance")]
public sealed record RegisterInstance(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Subject,
    [property: Id(2)] string Role,
    [property: Id(3)] string? Bundle = null,
    [property: Id(4)] bool Enabled = true,
    [property: Id(5)] string? Note = null) : RequestSynapse<InstanceRegistered>;

[GenerateSerializer]
[Alias("db.instance-registered")]
public sealed record InstanceRegistered(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] RegisteredInstance Instance) : Synapse;

[GenerateSerializer]
[Alias("db.retire-instance")]
public sealed record RetireInstance(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Subject) : RequestSynapse<InstanceRetired>;

[GenerateSerializer]
[Alias("db.instance-retired")]
public sealed record InstanceRetired(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Subject) : Synapse;

[GenerateSerializer]
[Alias("db.set-instance-enabled")]
public sealed record SetInstanceEnabled(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Subject,
    [property: Id(2)] bool Enabled) : RequestSynapse<InstanceEnabledChanged>;

[GenerateSerializer]
[Alias("db.instance-enabled-changed")]
public sealed record InstanceEnabledChanged(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] RegisteredInstance Instance) : Synapse;

// One request: copy structure of a named bundle as disabled instances (+ optional wires).
[GenerateSerializer]
[Alias("db.install-bundle")]
public sealed record InstallBundle(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Name,
    [property: Id(2)] BundleMember[] Members,
    [property: Id(3)] BundleWire[] Wires,
    [property: Id(4)] string? Intent = null) : RequestSynapse<BundleInstalled>;

[GenerateSerializer]
[Alias("db.bundle-installed")]
public sealed record BundleInstalled(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Name,
    [property: Id(2)] int MemberCount,
    [property: Id(3)] int WireCount,
    [property: Id(4)] bool Enabled) : Synapse;

[GenerateSerializer]
[Alias("db.bundle-member")]
public sealed record BundleMember(
    [property: Id(0)] string GrainType,
    [property: Id(1)] string Name,
    [property: Id(2)] string Role,
    [property: Id(3)] string? Note = null);

[GenerateSerializer]
[Alias("db.bundle-wire")]
public sealed record BundleWire(
    [property: Id(0)] string SourceType,
    [property: Id(1)] string SourceName,
    [property: Id(2)] string SynapseAlias,
    [property: Id(3)] string TargetType,
    [property: Id(4)] string TargetName,
    [property: Id(5)] string? Transform = null);

[GenerateSerializer]
[Alias("db.registered-instance")]
public sealed record RegisteredInstance(
    [property: Id(0)] NeuronId Subject,
    [property: Id(1)] string Role,
    [property: Id(2)] string? Bundle,
    [property: Id(3)] bool Enabled,
    [property: Id(4)] DateTimeOffset RegisteredAt,
    [property: Id(5)] string? Note);

[GenerateSerializer]
[Alias("db.list-instances")]
public sealed record ListInstances(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<InstancesListed>;

[GenerateSerializer]
[Alias("db.instances-listed")]
public sealed record InstancesListed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] RegisteredInstance[] Items) : Synapse;
