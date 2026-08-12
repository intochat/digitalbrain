namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.workspace.read-membership")]
public sealed record ReadMembership(
    [property: Id(0)] ActorContext Actor) : RequestSynapse<Membership>;

