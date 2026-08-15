namespace DigitalBrain.Abstractions.Registry;

[GenerateSerializer]
[Alias("db.list-instances")]
public sealed record ListInstances(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<InstancesListed>;

