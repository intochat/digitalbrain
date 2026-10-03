using System.Reflection;
using DigitalBrain.Kernel;
using Pulumi;
using Pulumi.AzureNative.App;
using Pulumi.AzureNative.App.Inputs;
using Pulumi.AzureNative.ManagedIdentity;
using Pulumi.AzureNative.Network;
using AppManagedServiceIdentityType = Pulumi.AzureNative.App.ManagedServiceIdentityType;
using KeyVaultSecret = Pulumi.AzureNative.KeyVault.Secret;
using KeyVaultSecretArgs = Pulumi.AzureNative.KeyVault.SecretArgs;
using KeyVaultSecretPropertiesArgs = Pulumi.AzureNative.KeyVault.Inputs.SecretPropertiesArgs;

namespace DigitalBrain.Deployment;

// Deploys a brain from its AppHost manifest: DeploymentKit lays the foundation, each composed module
// deploys what it owns, every secret lands in Key Vault, and the runtime app reads its whole manifest
// environment from there.
public static class DigitalBrainDeployment
{
    internal const string RuntimeHttpPort = "8080";
    private const string KeyVaultSecretsUser = "4633458b-17de-408a-b874-0445c86b69e6";
    private const string StorageBlobDataContributor = "ba92f5b4-2d11-453d-a403-e96b0029c9fe";
    private const string StorageTableDataContributor = "0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3";
    private const string AcrPull = "7f951dda-4ed3-4680-a7ca-43fe172d538d";

    public static async Task<Dictionary<string, object?>> RunAsync(DigitalBrainDeploymentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var manifest = options.Manifest;
        var runtime = manifest.Runtime;
        var modules = options.Modules ?? Discover(manifest.Modules(runtime));
        var kit = await DeploymentKitFoundation.DeployAsync(options, modules).ConfigureAwait(false);
        var prefix = options.NamingPrefix;

        var identity = new UserAssignedIdentity(prefix + "-runtime", new UserAssignedIdentityArgs
        {
            ResourceName = prefix + "-runtime",
            ResourceGroupName = kit.ResourceGroupName,
            Location = options.Location,
        });
        // Container Apps needs a workload-profiles environment for session pools and extra ports, on a subnet
        // delegated to it; DeploymentKit's own environment is consumption-only.
        var subnet = new Subnet(prefix + "-apps", new SubnetArgs
        {
            SubnetName = prefix + "-apps",
            ResourceGroupName = kit.ResourceGroupName,
            VirtualNetworkName = kit.Network.VirtualNetworkName,
            // Inside the kit VNet (10.0.0.0/16) and clear of its default subnets (10.0.0.0 to 10.0.6.255).
            AddressPrefix = options.Parameters.Get("appsSubnet") ?? "10.0.8.0/23",
            Delegations = [new Pulumi.AzureNative.Network.Inputs.DelegationArgs { Name = "apps", ServiceName = "Microsoft.App/environments" }],
        });
        var environment = new ManagedEnvironment(prefix + "-apps", new ManagedEnvironmentArgs
        {
            EnvironmentName = prefix + "-apps",
            ResourceGroupName = kit.ResourceGroupName,
            Location = options.Location,
            WorkloadProfiles = [new WorkloadProfileArgs { Name = "Consumption", WorkloadProfileType = "Consumption" }],
            VnetConfiguration = new VnetConfigurationArgs { InfrastructureSubnetId = subnet.Id, Internal = false },
        });
        var runtimeName = prefix + "-brain";
        var foundation = new DeploymentFoundation(
            prefix, options.Location, options.SubscriptionId, kit.ResourceGroupName, kit.Network.VirtualNetworkName,
            kit.KeyVault.VaultName, kit.KeyVault.VaultUri, kit.ContainerRegistry.LoginServer, kit.Storage.AccountName, kit.Storage.PrimaryKey,
            environment.Id, environment.DefaultDomain, identity.Id, identity.PrincipalId, identity.ClientId,
            environment.DefaultDomain.Apply(domain => $"https://{runtimeName}.{domain}"),
            kit.Database?.HostName is not null
                ? new DatabaseFoundation(kit.Database.ServerName, kit.Database.DatabaseName, kit.Database.FullyQualifiedDomainName, kit.Database.AdminUsername, kit.Database.Password)
                : null);

        var manifestValues = new ManifestValues(manifest, name => manifest.IsSecretParameter(name)
            ? options.Parameters.RequireSecret(name)
            : Output.Create(options.Parameters.Require(name)));
        ProvideRuntime(manifestValues, manifest, runtime, foundation);
        var cloudOnlyEnvironment = new Dictionary<string, (Input<string> Value, bool Secret)>(StringComparer.Ordinal)
        {
            ["AZURE_CLIENT_ID"] = (foundation.RuntimeIdentityClientId, false),
            ["DigitalBrain__KeyVault__Uri"] = (foundation.KeyVaultUri, false),
            // A local manifest carries a throwaway cluster id; the cloud cluster keeps one across deployments.
            ["Orleans__ClusterId"] = (options.Name, false),
            ["Orleans__ServiceId"] = (options.Name, false),
        };
        var context = new ModuleDeploymentContext(foundation, manifest, manifestValues, options.Parameters, cloudOnlyEnvironment);
        GrantFoundation(context, kit);
        foreach (var module in modules) { module.Deploy(context); }

        var containerSecrets = new InputList<SecretArgs>();
        var environmentVariables = new InputList<EnvironmentVarArgs>();
        // What modules set for the cloud replaces the manifest's local value of the same name.
        var runtimeVariables = manifest.Environment(runtime)
            .Where(variable => !cloudOnlyEnvironment.ContainsKey(variable.Key))
            .Select(variable => (variable.Key, Value: (Input<string>)manifestValues.Resolve(variable.Value), Secret: manifestValues.IsSecret(variable.Value)))
            .Concat(cloudOnlyEnvironment.Select(variable => (variable.Key, variable.Value.Value, variable.Value.Secret)))
            .ToList();
        if (manifestValues.Missing.Count > 0)
        {
            throw new InvalidOperationException("No module deploys what the runtime reads from: "
                + string.Join(", ", manifestValues.Missing.Order(StringComparer.Ordinal))
                + ". Give the owning module an IDigitalBrainModuleDeployment that provides these values.");
        }
        foreach (var (name, value, secret) in runtimeVariables)
        {
            if (!secret)
            {
                environmentVariables.Add(new EnvironmentVarArgs { Name = name, Value = value });
                continue;
            }
            var secretName = ModuleDeploymentContext.SecretName(name);
            var vaultSecret = new KeyVaultSecret(prefix + "-" + secretName, new KeyVaultSecretArgs
            {
                SecretName = secretName,
                VaultName = foundation.KeyVaultName,
                ResourceGroupName = foundation.ResourceGroupName,
                Properties = new KeyVaultSecretPropertiesArgs { Value = value },
            });
            containerSecrets.Add(new SecretArgs { Name = secretName, KeyVaultUrl = vaultSecret.Properties.Apply(properties => properties.SecretUri), Identity = foundation.RuntimeIdentityId });
            environmentVariables.Add(new EnvironmentVarArgs { Name = name, SecretRef = secretName });
        }

        var app = new ContainerApp(runtimeName, new ContainerAppArgs
        {
            ContainerAppName = runtimeName,
            ResourceGroupName = foundation.ResourceGroupName,
            EnvironmentId = foundation.EnvironmentId,
            WorkloadProfileName = "Consumption",
            Identity = new Pulumi.AzureNative.App.Inputs.ManagedServiceIdentityArgs
            {
                Type = AppManagedServiceIdentityType.UserAssigned,
                UserAssignedIdentities = [foundation.RuntimeIdentityId],
            },
            Configuration = new ConfigurationArgs
            {
                Ingress = new IngressArgs { External = true, TargetPort = int.Parse(RuntimeHttpPort, System.Globalization.CultureInfo.InvariantCulture), Transport = "auto" },
                Secrets = containerSecrets,
                Registries = [new RegistryCredentialsArgs { Server = foundation.RegistryLoginServer, Identity = foundation.RuntimeIdentityId }],
            },
            Template = new TemplateArgs
            {
                Containers = [new ContainerArgs { Name = "brain", Image = options.RuntimeImage, Env = environmentVariables, Resources = new ContainerResourcesArgs { Cpu = 2, Memory = "4Gi" } }],
                // One silo: Orleans membership and the Aspire bridge assume a single runtime replica.
                Scale = new ScaleArgs { MinReplicas = 1, MaxReplicas = 1 },
            },
        }, new CustomResourceOptions { DependsOn = [.. context.RuntimeGrants] });

        return new Dictionary<string, object?>
        {
            ["resourceGroupName"] = foundation.ResourceGroupName,
            ["runtimeUrl"] = foundation.RuntimeUrl,
            ["runtimeApp"] = app.Name,
            ["registryLoginServer"] = foundation.RegistryLoginServer,
            ["keyVaultUri"] = foundation.KeyVaultUri,
        };
    }

    // The runtime's own bindings and the brain's Orleans storage (the Aspire "storage" resource).
    private static void ProvideRuntime(ManifestValues values, AspireManifest manifest, string runtime, DeploymentFoundation foundation)
    {
        values.Provide(runtime, "bindings.http.targetPort", RuntimeHttpPort);
        values.Provide(runtime, "bindings.http.url", foundation.RuntimeUrl);
        values.Provide(runtime, "bindings.orleans-silo.targetPort", "11111");
        values.Provide(runtime, "bindings.orleans-gateway.targetPort", "30000");
        foreach (var storage in manifest.ResourceNames.Where(name => manifest.TypeOf(name) == "azure.bicep.v0"
            && manifest.Resource(name).TryGetProperty("path", out var path) && path.GetString()!.Contains("storage", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith("-roles", StringComparison.Ordinal)))
        {
            values.Provide(storage, "outputs.tableEndpoint", foundation.StorageAccountName.Apply(account => $"https://{account}.table.core.windows.net/"));
            values.Provide(storage, "outputs.blobEndpoint", foundation.StorageAccountName.Apply(account => $"https://{account}.blob.core.windows.net/"));
            values.Provide(storage, "outputs.queueEndpoint", foundation.StorageAccountName.Apply(account => $"https://{account}.queue.core.windows.net/"));
        }
    }

    private static void GrantFoundation(ModuleDeploymentContext context, DeploymentKit.Models.Outputs.InfrastructureDeploymentOutputs kit)
    {
        var storageAccount = context.Foundation.ResourceId("Microsoft.Storage/storageAccounts", context.Foundation.StorageAccountName);
        context.GrantRuntime("vault-secrets", KeyVaultSecretsUser, kit.KeyVault.ResourceId);
        context.GrantRuntime("storage-blobs", StorageBlobDataContributor, storageAccount);
        context.GrantRuntime("storage-tables", StorageTableDataContributor, storageAccount);
        context.GrantRuntime("registry-pull", AcrPull, kit.ContainerRegistry.ResourceId);
    }

    private static List<IDigitalBrainModuleDeployment> Discover(IReadOnlyList<string> moduleTypes)
    {
        List<IDigitalBrainModuleDeployment> deployments = [];
        foreach (var moduleType in moduleTypes)
        {
            var module = System.Type.GetType(moduleType, throwOnError: true)!;
            if (module.GetCustomAttribute<ModuleDeploymentAttribute>() is not { } attribute) { continue; }
            var deployment = System.Type.GetType(attribute.TypeName)
                ?? throw new InvalidOperationException($"{module.Name} deploys with '{attribute.TypeName}', which is not in this deployment program. Reference the module's Deployment project.");
            deployments.Add((IDigitalBrainModuleDeployment)Activator.CreateInstance(deployment)!);
        }
        return deployments;
    }
}
