namespace DigitalBrain.Abstractions.Integrations;

// External-system credential binding (not a ConnectionGraph edge). Tokens are never
// journaled ΓÇö only this record's ProtectedTokenReference (protector purpose) is.
[GenerateSerializer]
[Alias("db.integration")]
public sealed record Integration(
    [property: Id(0)] string Provider,
    [property: Id(1)] IntegrationScope Scope,
    [property: Id(2)] string SubjectId,
    [property: Id(3)] string? ExternalAccount,
    [property: Id(4)] string[] GrantedScopes,
    [property: Id(5)] string ProtectedTokenReference);

