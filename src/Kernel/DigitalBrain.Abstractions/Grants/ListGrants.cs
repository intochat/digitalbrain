namespace DigitalBrain.Abstractions.Grants;

[GenerateSerializer]
[Alias("db.list-grants")]
public sealed record ListGrants(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<GrantsListed>;

