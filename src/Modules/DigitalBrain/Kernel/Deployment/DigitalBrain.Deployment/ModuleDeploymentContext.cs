using Pulumi;
using Pulumi.AzureNative.App;
using Pulumi.AzureNative.App.Inputs;
using Pulumi.AzureNative.Authorization;
using Pulumi.AzureNative.Storage;

namespace DigitalBrain.Deployment;

public sealed class ModuleDeploymentContext
{
    private readonly Dictionary<string, (Input<string> Value, bool Secret)> _runtimeEnvironment;
    private readonly List<Resource> _runtimeGrants = [];

    internal ModuleDeploymentContext(DeploymentFoundation foundation, AspireManifest manifest, ManifestValues values, Config settings,
        Dictionary<string, (Input<string> Value, bool Secret)> runtimeEnvironment)
    {
        Settings = settings;
        Foundation = foundation;
        Manifest = manifest;
        Values = values;
        _runtimeEnvironment = runtimeEnvironment;
    }

    public DeploymentFoundation Foundation { get; }

    public AspireManifest Manifest { get; }

    public ManifestValues Values { get; }

    public string Runtime => Manifest.Runtime;

    // Stack config: the manifest's parameters by their AppHost names, plus module deployment settings.
    public Config Settings { get; }

    // The value of a module parameter as the AppHost declared it (secret parameters stay secret).
    public Output<string> Parameter(string name) => Values.Resolve("{" + name + ".value}");

    // Supplies what the runtime's manifest environment reads from a resource this module deploys.
    public void Provide(string resource, string path, Input<string> value, bool secret = false) => Values.Provide(resource, path, value, secret);

    // Environment the runtime needs in the cloud beyond what the manifest already carries.
    public void SetRuntimeEnvironment(string name, Input<string> value, bool secret = false)
        => _runtimeEnvironment[name] = (secret ? Output.CreateSecret(value.ToOutput()) : value, secret);

    // What runs as the runtime identity (the runtime app, pools pulling images) waits for its role assignments.
    public IReadOnlyList<Resource> RuntimeGrants => _runtimeGrants;

    public RoleAssignment GrantRuntime(string name, string roleDefinitionId, Input<string> scope)
    {
        var grant = new RoleAssignment(Foundation.NamingPrefix + "-" + name, new RoleAssignmentArgs
        {
            PrincipalId = Foundation.RuntimeIdentityPrincipalId,
            PrincipalType = PrincipalType.ServicePrincipal,
            RoleDefinitionId = $"/subscriptions/{Foundation.SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{roleDefinitionId}",
            Scope = scope,
        });
        _runtimeGrants.Add(grant);
        return grant;
    }

    // A backing service (database, vector store, model server) the runtime reaches inside the environment.
    public ContainerApp AddService(ServiceDefinition service)
    {
        ArgumentNullException.ThrowIfNull(service);
        var appName = Foundation.NamingPrefix + "-" + service.Name;
        var volumes = new InputList<VolumeArgs>();
        var mounts = new InputList<VolumeMountArgs>();
        if (service.DataPath is { } dataPath)
        {
            var share = new Pulumi.AzureNative.Storage.FileShare(appName + "-data", new FileShareArgs
            {
                AccountName = Foundation.StorageAccountName,
                ResourceGroupName = Foundation.ResourceGroupName,
                ShareName = appName + "-data",
                ShareQuota = service.DataQuotaGb,
            });
            var storage = new ManagedEnvironmentsStorage(appName + "-data", new ManagedEnvironmentsStorageArgs
            {
                EnvironmentName = Foundation.EnvironmentId.Apply(id => id.Split('/')[^1]),
                ResourceGroupName = Foundation.ResourceGroupName,
                StorageName = service.Name + "-data",
                Properties = new ManagedEnvironmentStoragePropertiesArgs
                {
                    AzureFile = new AzureFilePropertiesArgs
                    {
                        AccountName = Foundation.StorageAccountName,
                        AccountKey = Foundation.StorageAccountKey,
                        ShareName = share.Name,
                        AccessMode = AccessMode.ReadWrite,
                    },
                },
            });
            volumes.Add(new VolumeArgs { Name = "data", StorageName = storage.Name, StorageType = StorageType.AzureFile });
            mounts.Add(new VolumeMountArgs { VolumeName = "data", MountPath = dataPath });
        }
        return new ContainerApp(appName, new ContainerAppArgs
        {
            ContainerAppName = appName,
            ResourceGroupName = Foundation.ResourceGroupName,
            EnvironmentId = Foundation.EnvironmentId,
            WorkloadProfileName = "Consumption",
            Configuration = new ConfigurationArgs
            {
                Ingress = new IngressArgs
                {
                    External = false,
                    TargetPort = service.Port,
                    Transport = service.Transport,
                    AllowInsecure = service.AllowInsecure,
                    AdditionalPortMappings = [.. service.AdditionalPorts.Select(port => new IngressPortMappingArgs { External = false, TargetPort = port, ExposedPort = port })],
                },
                Secrets = [.. service.Secrets.Select(secret => new SecretArgs { Name = SecretName(secret.Key), Value = secret.Value })],
            },
            Template = new TemplateArgs
            {
                Containers =
                [
                    new ContainerArgs
                    {
                        Name = service.Name,
                        Image = service.Image,
                        Command = service.Command,
                        Args = service.Arguments,
                        Env = [.. service.Environment.Select(variable => new EnvironmentVarArgs { Name = variable.Key, Value = variable.Value })
                            .Concat(service.Secrets.Select(secret => new EnvironmentVarArgs { Name = secret.Key, SecretRef = SecretName(secret.Key) }))],
                        Resources = new ContainerResourcesArgs { Cpu = service.Cpu, Memory = service.Memory },
                        VolumeMounts = mounts,
                    },
                ],
                Volumes = volumes,
                Scale = new ScaleArgs { MinReplicas = 1, MaxReplicas = 1 },
            },
        });
    }

    // Container Apps secret names are lowercase letters, digits and dashes.
    internal static string SecretName(string variable) => variable.ToLowerInvariant().Replace('_', '-');

    // Internal ingress: the primary port answers on the app's internal FQDN, extra ports on the app name.
    public Output<string> InternalHost(ContainerApp app) => Output.Tuple(app.Name, Foundation.EnvironmentDefaultDomain).Apply(parts => $"{parts.Item1}.internal.{parts.Item2}");
}

public sealed record ServiceDefinition(string Name, string Image, int Port)
{
    public string Transport { get; init; } = "auto";
    // Plain HTTP on the internal ingress (port 80), for clients that dial http:// inside the environment.
    public bool AllowInsecure { get; init; }
    public InputList<string> Command { get; init; } = new();
    public IReadOnlyList<int> AdditionalPorts { get; init; } = [];
    public IReadOnlyDictionary<string, Input<string>> Environment { get; init; } = new Dictionary<string, Input<string>>();
    // Environment variables whose values are kept as Container Apps secrets.
    public IReadOnlyDictionary<string, Input<string>> Secrets { get; init; } = new Dictionary<string, Input<string>>();
    public InputList<string> Arguments { get; init; } = new();
    public string? DataPath { get; init; }
    public int DataQuotaGb { get; init; } = 32;
    public double Cpu { get; init; } = 1;
    public string Memory { get; init; } = "2Gi";
}
