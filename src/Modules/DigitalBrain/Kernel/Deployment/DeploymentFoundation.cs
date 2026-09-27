using Pulumi;

namespace DigitalBrain.Deployment;

// What every module deploys into: DeploymentKit's resource group, network, vault, registry and storage,
// plus the Container Apps environment and identity the brain runtime runs with.
public sealed record DeploymentFoundation(
    string NamingPrefix,
    string Location,
    string SubscriptionId,
    Output<string> ResourceGroupName,
    Output<string> VirtualNetworkName,
    Output<string> KeyVaultName,
    Output<string> KeyVaultUri,
    Output<string> RegistryLoginServer,
    Output<string> StorageAccountName,
    Output<string> StorageAccountKey,
    Output<string> EnvironmentId,
    Output<string> EnvironmentDefaultDomain,
    Output<string> RuntimeIdentityId,
    Output<string> RuntimeIdentityPrincipalId,
    Output<string> RuntimeIdentityClientId,
    Output<string> RuntimeUrl,
    DatabaseFoundation? Database)
{
    public Output<string> ResourceId(string provider, Output<string> name)
        => Output.Tuple(ResourceGroupName, name).Apply(parts => $"/subscriptions/{SubscriptionId}/resourceGroups/{parts.Item1}/providers/{provider}/{parts.Item2}");
}

public sealed record DatabaseFoundation(string ServerName, string DatabaseName, Output<string> Host, string AdminUser, Output<string> AdminPassword);
