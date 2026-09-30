using DigitalBrain.Contracts.Types;

namespace DigitalBrain.Sdk.Integrations;

// Field name to vault reference, plus the values of non-secret settings. Never a secret value.
[GenerateSerializer, Alias("integration.registration.state")]
internal sealed class RegistrationState
{
    [Id(0)] public Dictionary<string, SecretRef> References { get; set; } = new(StringComparer.Ordinal);

    // Non-secret setting values only; secret fields never appear here.
    [Id(1)] public Dictionary<string, string> Settings { get; set; } = new(StringComparer.Ordinal);

    // Bumped by every write, so holders of a released secret can tell a rotation from no change.
    [Id(2)] public long Revision { get; set; }
}

internal static class IntegrationVault
{
    // ":" cannot occur in a principal id (^[a-z0-9][a-z0-9-]*$), so no account can claim this vault.
    public const string Owner = ":integrations";
    public const string GrainKeyPrefix = "integration/";
    public const string CallerAppId = "integration.registration";

    public static string SecretName(string integrationId, string field) => $"integration.{integrationId}.{field}";
}
