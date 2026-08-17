namespace DigitalBrain.Abstractions.Registry;

[GenerateSerializer]
[Alias("db.set-instance-enabled")]
public sealed record SetInstanceEnabled(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Subject,
    [property: Id(2)] bool Enabled) : RequestSynapse<InstanceEnabledChanged>;

