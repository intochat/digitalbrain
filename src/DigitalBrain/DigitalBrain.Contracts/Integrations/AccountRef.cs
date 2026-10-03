namespace DigitalBrain.Contracts.Integrations;

// An opaque account. Kind and id name the credential the platform holds; the value never crosses this type.
[GenerateSerializer, Alias("integrations.account-ref")]
public sealed record AccountRef([property: Id(0)] string Kind, [property: Id(1)] string Id);
