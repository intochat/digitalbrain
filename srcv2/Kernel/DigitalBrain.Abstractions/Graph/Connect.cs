namespace DigitalBrain.Abstractions.Graph;

[GenerateSerializer]
[Alias("db.connect")]
public sealed record Connect(
    [property: Id(0)] Guid ConnectionId,
    [property: Id(1)] NeuronId Source,
    [property: Id(2)] string SynapseAlias,
    [property: Id(3)] NeuronId Target,
    [property: Id(4)] string? Transform = null,
    [property: Id(5)] DateTimeOffset? ExpiresAt = null,
    [property: Id(6)] string? Intent = null) : RequestSynapse<Connected>;
