using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Google;

[GenerateSerializer]
public sealed record OAuthConsentRequired([property: Id(1)] string UserAccountId,
    [property: Id(2)] string ConsentUrl,
    [property: Id(3)] IReadOnlyList<string> Scopes
) : Synapse;
