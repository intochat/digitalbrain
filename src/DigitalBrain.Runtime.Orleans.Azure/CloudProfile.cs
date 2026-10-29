using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Storage;

namespace DigitalBrain.Runtime.Orleans.Azure;

/// <summary>
/// Hosted in the cloud (P2-D-12 revised, P2-D-22): Azure Table clustering and reminders, Azure Blob grain storage for
/// <c>platform</c>, <c>PubSubStore</c> and every module grain store, Azure Queue streams for <c>signals</c>, Key Vault secrets as
/// configuration, and blob-lease brain ownership. One account (<c>IntoChatOS:Cloud:Storage</c>) carries tables, blobs and queues.
/// The <c>cluster</c> store is host-bound (BrainLog); until the host registers it, it is <see cref="UnregisteredClusterStorage"/>.
/// </summary>
public sealed class CloudProfile : ITopologyProfile
{
    /// <summary>The blob container holding one lease blob per brain; provisioned with the environment.</summary>
    public const string LeaseContainer = "brain-leases";

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        IntoChatOSNames.PlatformGrainStorage,
        IntoChatOSNames.PubSubStore,
    };

    private readonly Action<IConfigurationBuilder, Uri>? _keyVaultSource;

    /// <summary>The production topology: Key Vault through <c>DefaultAzureCredential</c>.</summary>
    public CloudProfile() : this(null) { }

    /// <summary>
    /// A cloud topology whose Key Vault source is replaced (tests on Azurite only; production code never passes one).
    /// Null keeps the production source.
    /// </summary>
    public CloudProfile(Action<IConfigurationBuilder, Uri>? keyVaultSource) => _keyVaultSource = keyVaultSource;

    /// <summary>The signal queues (one partition).</summary>
    public static IReadOnlyList<string> SignalQueues { get; } = ["signals-0"];

    /// <summary>The Azure resource kinds the cloud topology needs.</summary>
    public static IReadOnlyList<string> AzureResourceKinds { get; } =
        ["Microsoft.Storage/storageAccounts", "Microsoft.KeyVault/vaults", "Microsoft.App/managedEnvironments"];

    /// <inheritdoc />
    public Topology Topology => Topology.Cloud;

    /// <summary>The cloud bindings.</summary>
    public static Bindings CloudBindings { get; } = new(
        ClusteringBinding.AzureTable,
        GrainStoreBinding.AzureBlob,
        StreamsBinding.AzureQueue,
        RemindersBinding.AzureTable,
        SecretsBinding.KeyVault,
        HttpBinding.Direct,
        SandboxBinding.HostedContainer,
        ModelBinding.ConfiguredEndpoint,
        UiBinding.HostedWeb)
    { ClusterStore = ClusterStoreBinding.HostBound };

    /// <inheritdoc />
    public Bindings Bindings => CloudBindings;

    /// <summary>The cloud process shape.</summary>
    public static Services CloudServices { get; } = new(
        [
            new ServiceProcess("os", LocalProfile.Image, [TopologyContext.DefaultSiloPort, TopologyContext.DefaultGatewayPort, LocalProfile.HttpPort], [],
                [EnvironmentName(IntoChatOSNames.TopologyKey), EnvironmentName(IntoChatOSNames.CloudStorageKey),
                 EnvironmentName(IntoChatOSNames.CloudKeyVaultKey), "AZURE_CLIENT_ID"]),
            new ServiceProcess("os-sandbox", LocalProfile.SandboxImage, [], [], []),
        ],
        AzureResourceKinds);

    /// <inheritdoc />
    public Services Services => CloudServices;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string?> Defaults { get; } = new Dictionary<string, string?>();

    /// <inheritdoc />
    public IEnumerable<TopologyIssue> Validate(IConfiguration configuration, IReadOnlyList<ModuleManifest> modules)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(modules);
        var issues = new List<TopologyIssue>();
        if (CloudStorageAccount.TryParse(configuration[IntoChatOSNames.CloudStorageKey], out var storage) is null) { issues.Add(storage!); }
        var vaultValue = configuration[IntoChatOSNames.CloudKeyVaultKey];
        if (!string.IsNullOrWhiteSpace(vaultValue) && KeyVault(vaultValue, out var vault) is null) { issues.Add(vault!); }
        foreach (var module in modules)
        {
            foreach (var need in module.Needs.Where(n => n.Kind == NeedKind.State && n.Name is not null && Reserved.Contains(n.Name)))
            {
                issues.Add(new TopologyIssue($"module {module.Id ?? "(unnamed)"}",
                    $"grain store {need.Name} is reserved by the runtime; choose another name."));
            }
        }
        return issues;
    }

    /// <summary>The key vault URI, or null with an issue naming <c>IntoChatOS:Cloud:KeyVault</c>.</summary>
    public static Uri? KeyVault(string? value, out TopologyIssue? issue)
    {
        issue = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            issue = new TopologyIssue(IntoChatOSNames.CloudKeyVaultKey, "is required on Topology=cloud (https://<vault>.vault.azure.net/).");
            return null;
        }
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) { return uri; }
        issue = new TopologyIssue(IntoChatOSNames.CloudKeyVaultKey, "must be an absolute https URI.");
        return null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Secret <c>IntoChatOS--MasterKey</c> maps to <c>IntoChatOS:MasterKey</c>; secrets load once (no reload interval).
    /// Without <c>IntoChatOS:Cloud:KeyVault</c> (contributor runs on Azurite, 04-CONTEXT R4-08) secrets come from the machine key,
    /// as on local; the topology settings carry the warning.
    /// </remarks>
    public void ConfigureConfiguration(IConfigurationBuilder builder, TopologyContext context)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.Configuration[IntoChatOSNames.CloudKeyVaultKey]))
        {
            builder.Add(new MachineKeyConfigurationSource(context.Home, context.Configuration));
            return;
        }
        var vault = KeyVault(context.Configuration[IntoChatOSNames.CloudKeyVaultKey], out var issue)
            ?? throw new InvalidOperationException($"{issue!.Key}: {issue.Message}");
        if (_keyVaultSource is { } replace) { replace(builder, vault); }
        else { builder.AddAzureKeyVault(vault, CloudStorageAccount.Credential()); }
    }

    /// <inheritdoc />
    public void ConfigureSilo(ISiloBuilder silo, TopologyContext context)
    {
        ArgumentNullException.ThrowIfNull(silo);
        ArgumentNullException.ThrowIfNull(context);
        var account = CloudStorageAccount.Parse(context.Configuration[IntoChatOSNames.CloudStorageKey]);
        silo.Services.TryAddSingleton(account);
        silo.Configure<EndpointOptions>(options =>
        {
            options.SiloPort = context.SiloPort;
            options.GatewayPort = context.GatewayPort;
        });

        silo.UseAzureStorageClustering(options => options.Configure(o => o.TableServiceClient = account.Tables()));
        silo.UseAzureTableReminderService(options => options.Configure(o => o.TableServiceClient = account.Tables()));

        var stores = new List<string> { IntoChatOSNames.PlatformGrainStorage, IntoChatOSNames.PubSubStore };
        stores.AddRange(context.Modules.SelectMany(m => m.Needs)
            .Where(n => n.Kind == NeedKind.State && n.Name is not null && n.Name != IntoChatOSNames.ClusterGrainStorage && !Reserved.Contains(n.Name))
            .Select(n => n.Name!));
        foreach (var name in stores.Distinct(StringComparer.Ordinal))
        {
            silo.AddAzureBlobGrainStorage(name, options => options.Configure(o =>
            {
                o.BlobServiceClient = account.Blobs();
                o.ContainerName = IntoChatOSNames.GrainStateContainer;
            }));
        }
        // Replaced by the host-bound registration, which ConfigureTopologySilo runs after this.
        silo.Services.AddKeyedSingleton<IGrainStorage>(IntoChatOSNames.ClusterGrainStorage, new UnregisteredClusterStorage());

        silo.AddAzureQueueStreams(IntoChatOSNames.SignalStreams, configurator => configurator.ConfigureAzureQueue(options => options.Configure(o =>
        {
            o.QueueServiceClient = account.Queues();
            o.QueueNames = [.. SignalQueues];
        })));
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, TopologyContext context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);
        var account = CloudStorageAccount.Parse(context.Configuration[IntoChatOSNames.CloudStorageKey]);
        services.TryAddSingleton(account);
        services.TryAddSingleton(TimeProvider.System);
        services.RemoveAll<IBrainLogOwnership>();
        services.RemoveAll<IBrainOwnershipCounters>();
        services.AddSingleton(sp => new BlobLeaseBrainLogOwnership(
            sp.GetRequiredService<CloudStorageAccount>().Blobs().GetBlobContainerClient(LeaseContainer),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
            BlobLeaseBrainLogOwnership.DefaultClaimTimeout,
            BlobLeaseBrainLogOwnership.DefaultWriteTimeout));
        services.AddSingleton<IBrainLogOwnership>(sp => sp.GetRequiredService<BlobLeaseBrainLogOwnership>());
        services.AddSingleton<IBrainOwnershipCounters>(sp => sp.GetRequiredService<BlobLeaseBrainLogOwnership>());
    }

    private static string EnvironmentName(string key) => key.Replace(":", "__", StringComparison.Ordinal);
}
