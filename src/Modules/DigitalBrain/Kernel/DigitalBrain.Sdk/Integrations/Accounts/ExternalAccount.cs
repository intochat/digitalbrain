using System.Text.Json.Serialization;

namespace DigitalBrain.Sdk.Integrations.Accounts;

// An account that lives outside the vault-backed registry (an OAuth grant held by its own neuron).
// The integration's module contributes it so the accounts surface stays integration-neutral.
public interface IExternalAccount
{
    string ConnectionId { get; }

    Task<AccountRow?> ReadAsync(IGrainFactory grains, CancellationToken cancellationToken);

    Task DisconnectAsync(IGrainFactory grains, CancellationToken cancellationToken);
}

public sealed record AccountRow(
    string Id,
    string IntegrationId,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? LastProbedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Account = null);
