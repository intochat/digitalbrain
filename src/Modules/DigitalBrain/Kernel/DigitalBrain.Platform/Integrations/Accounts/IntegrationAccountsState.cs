using DigitalBrain.Sdk.Integrations.Accounts;

namespace DigitalBrain.Platform.Integrations.Accounts;

[GenerateSerializer, Alias("connections.state")] // alias predates the integrations rename; persisted, do not touch
internal sealed record IntegrationAccountsState
{
    [Id(0)] public Dictionary<string, IntegrationAccount> Connections { get; init; } = new(StringComparer.Ordinal);
}
