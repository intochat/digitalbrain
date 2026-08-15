namespace DigitalBrain.Abstractions.Registry;

[GenerateSerializer]
[Alias("db.register-instance")]
public sealed record RegisterInstance(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Subject,
    [property: Id(2)] string Role,
    [property: Id(3)] string? Bundle = null,
    [property: Id(4)] bool Enabled = true,
    [property: Id(5)] string? Note = null) : RequestSynapse<InstanceRegistered>;

