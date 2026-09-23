using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

namespace IntoChat.AppHost;

// Hosted product deployment. Config-only: it reads managed-identity and Key Vault settings from
// configuration and forwards them to the runtime as environment variables. It never contacts Azure,
// so a local `aspire run` of the hosted profile stays offline.
internal static class HostedProfile
{
    public const string EnabledKey = "IntoChat:Hosted:Enabled";
    public const string KeyVaultUriKey = "IntoChat:Hosted:KeyVaultUri";
    public const string ManagedIdentityClientIdKey = "IntoChat:Hosted:ManagedIdentityClientId";
    public const string BackupPurgeCommandKey = "IntoChat:Backups:PurgeCommand";

    public static bool IsHosted(string profile, IConfiguration configuration)
        => string.Equals(profile, ProductSurfaceResources.HostedProfile, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("DIGITALBRAIN_HOSTED"), "1", StringComparison.Ordinal)
            || configuration.GetValue<bool>(EnabledKey);

    public static void Apply(IResourceBuilder<ProjectResource> runtime, IConfiguration configuration)
    {
        runtime.WithEnvironment("IntoChat__Hosted__Enabled", "true");
        Forward(runtime, configuration, KeyVaultUriKey, "DigitalBrain__KeyVault__Uri");
        Forward(runtime, configuration, ManagedIdentityClientIdKey, "AZURE_CLIENT_ID");
        Forward(runtime, configuration, BackupPurgeCommandKey, "IntoChat__Backups__PurgeCommand");
    }

    private static void Forward(IResourceBuilder<ProjectResource> runtime, IConfiguration configuration, string configurationKey, string environmentVariable)
    {
        if (configuration[configurationKey] is { Length: > 0 } value)
        {
            runtime.WithEnvironment(environmentVariable, value);
        }
    }
}
