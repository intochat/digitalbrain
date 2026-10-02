namespace DigitalBrain.Platform.Integrations.Accounts;

internal sealed record ConnectAccountInput(string? IntegrationId, string? ConnectionId, string? Label, string? Value, string? SecretReference);
