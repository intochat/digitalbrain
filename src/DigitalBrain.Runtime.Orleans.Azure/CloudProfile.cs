using DigitalBrain.Modules;
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
/// <c>platform</c> and every module grain store, Key Vault secrets as
/// configuration, and blob-lease brain ownership. One account (<see cref="RuntimeOptions.CloudStorageKey"/>) carries tables and blobs.
/// The <c>cluster</c> store is host-bound (BrainLog); until the host registers it, it is <see cref="UnregisteredClusterStorage"/>.
/// </summary>
public sealed class CloudProfile : ITopologyProfile, IRuntimeOptionsHolder
{
    /// <summary>The blob container holding one lease blob per brain; provisioned with the environment.</summary>
    public const string LeaseContainer = "brain-leases";

    private readonly HashSet<string> _reserved;
    private readonly RuntimeOptions _options;
    private readonly Action<IConfigurationBuilder, Uri>? _keyVaultSource;

    /// <summary>The production topology: Key Vault through <c>DefaultAzureCredential</c>, <see cref="RuntimeOptions.Default"/> names.</summary>
    public CloudProfile() : this(RuntimeOptions.Default, null) { }

    /// <summary>
    /// A cloud topology whose Key Vault source is replaced (tests on Azurite only; production code never passes one).
    /// Null keeps the production source.
    /// </summary>
    public CloudProfile(Action<IConfigurationBuilder, Uri>? keyVaultSource) : this(RuntimeOptions.Default, keyVaultSource) { }

    /// <summary>A cloud topology with the key and store names the host chose.</summary>
    /// <param name="options">The runtime's key and store names.</param>
    /// <param name="keyVaultSource">Replaces the production Key Vault source (tests on Azurite only); null keeps it.</param>
    public CloudProfile(RuntimeOptions options, Action<IConfigurationBuilder, Uri>? keyVaultSource)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _keyVaultSource = keyVaultSource;
        _reserved = new(StringComparer.OrdinalIgnoreCase) { _options.PlatformGrainStorage };
        Services = ServicesFor(_options);
    }

    /// <summary>The Azure resource kinds the cloud topology needs.</summary>
    public static IReadOnlyList<string> AzureResourceKinds { get; } =
        ["Microsoft.Storage/storageAccounts", "Microsoft.KeyVault/vaults", "Microsoft.App/managedEnvironments"];

    /// <inheritdoc />
    public RuntimeOptions Options => _options;

    /// <inheritdoc />
    public Topology Topology => Topology.Cloud;

    /// <summary>The cloud bindings.</summary>
    public static Bindings CloudBindings { get; } = new(
        ClusteringBinding.AzureTable,
        GrainStoreBinding.AzureBlob,
        RemindersBinding.AzureTable,
        SecretsBinding.KeyVault,
        HttpBinding.Direct,
        SandboxBinding.HostedContainer,
        ModelBinding.ConfiguredEndpoint,
        UiBinding.HostedWeb)
    { ClusterStore = ClusterStoreBinding.HostBound };

    /// <inheritdoc />
    public Bindings Bindings => CloudBindings;

    /// <summary>The cloud process shape with the <see cref="RuntimeOptions.Default"/> environment names.</summary>
    public static Services CloudServices { get; } = ServicesFor(RuntimeOptions.Default);

    /// <summary>The cloud process shape for <paramref name="options"/>.</summary>
    public static Services ServicesFor(RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(
            [
                new ServiceProcess("os", options.OsImage, [TopologyContext.DefaultSiloPort, TopologyContext.DefaultGatewayPort, LocalProfile.HttpPort], [],
                    [EnvironmentName(options.TopologyKey), EnvironmentName(options.CloudStorageKey),
                     EnvironmentName(options.CloudKeyVaultKey), "AZURE_CLIENT_ID"]),
                new ServiceProcess("os-sandbox", options.SandboxImage, [], [], []),
            ],
            AzureResourceKinds);
    }

    /// <inheritdoc />
    public Services Services { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string?> Defaults { get; } = new Dictionary<string, string?>();

    /// <inheritdoc />
    public IEnumerable<TopologyIssue> Validate(IConfiguration configuration, IReadOnlyList<ModuleManifest> modules)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(modules);
        var issues = new List<TopologyIssue>();
        if (CloudStorageAccount.TryParse(configuration[_options.CloudStorageKey], out var storage, _options) is null) { issues.Add(storage!); }
        var vaultValue = configuration[_options.CloudKeyVaultKey];
        if (!string.IsNullOrWhiteSpace(vaultValue) && KeyVault(vaultValue, out var vault, _options) is null) { issues.Add(vault!); }
        foreach (var module in modules)
        {
            foreach (var need in module.Needs.Where(n => n.Kind == NeedKind.State && n.Name is not null && _reserved.Contains(n.Name)))
            {
                issues.Add(new TopologyIssue($"module {module.Id ?? "(unnamed)"}",
                    $"grain store {need.Name} is reserved by the runtime; choose another name."));
            }
        }
        return issues;
    }

    /// <summary>The key vault URI, or null with an issue naming the cloud key vault key.</summary>
    /// <param name="value">The configured value.</param>
    /// <param name="issue">The issue when the value is missing or invalid.</param>
    /// <param name="options">Names the key vault key; null means <see cref="RuntimeOptions.Default"/>.</param>
    public static Uri? KeyVault(string? value, out TopologyIssue? issue, RuntimeOptions? options = null)
    {
        var key = (options ?? RuntimeOptions.Default).CloudKeyVaultKey;
        issue = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            issue = new TopologyIssue(key, "is required on Topology=cloud (https://<vault>.vault.azure.net/).");
            return null;
        }
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) { return uri; }
        issue = new TopologyIssue(key, "must be an absolute https URI.");
        return null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A Key Vault secret named like the master key with <c>--</c> for <c>:</c> maps to <see cref="RuntimeOptions.MasterKeyKey"/>; secrets load once (no reload interval).
    /// Without <see cref="RuntimeOptions.CloudKeyVaultKey"/> (contributor runs on Azurite, 04-CONTEXT R4-08) secrets come from the machine key,
    /// as on local; the topology settings carry the warning.
    /// </remarks>
    public void ConfigureConfiguration(IConfigurationBuilder builder, TopologyContext context)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.Configuration[_options.CloudKeyVaultKey]))
        {
            builder.Add(new MachineKeyConfigurationSource(context.Home, context.Configuration, options: _options));
            return;
        }
        var vault = KeyVault(context.Configuration[_options.CloudKeyVaultKey], out var issue, _options)
            ?? throw new InvalidOperationException($"{issue!.Key}: {issue.Message}");
        if (_keyVaultSource is { } replace) { replace(builder, vault); }
        else { builder.AddAzureKeyVault(vault, CloudStorageAccount.Credential()); }
    }

    /// <inheritdoc />
    public void ConfigureSilo(ISiloBuilder silo, TopologyContext context)
    {
        ArgumentNullException.ThrowIfNull(silo);
        ArgumentNullException.ThrowIfNull(context);
        var account = CloudStorageAccount.Parse(context.Configuration[_options.CloudStorageKey], _options);
        silo.Services.TryAddSingleton(account);
        silo.Configure<EndpointOptions>(options =>
        {
            options.SiloPort = context.SiloPort;
            options.GatewayPort = context.GatewayPort;
        });

        silo.UseAzureStorageClustering(options => options.Configure(o => o.TableServiceClient = account.Tables()));
        silo.UseAzureTableReminderService(options => options.Configure(o => o.TableServiceClient = account.Tables()));

        var stores = new List<string> { _options.PlatformGrainStorage };
        stores.AddRange(context.Modules.SelectMany(m => m.Needs)
            .Where(n => n.Kind == NeedKind.State && n.Name is not null && n.Name != _options.ClusterGrainStorage && !_reserved.Contains(n.Name))
            .Select(n => n.Name!));
        foreach (var name in stores.Distinct(StringComparer.Ordinal))
        {
            silo.AddAzureBlobGrainStorage(name, options => options.Configure(o =>
            {
                o.BlobServiceClient = account.Blobs();
                o.ContainerName = _options.GrainStateContainer;
            }));
        }
        // Replaced by the host-bound registration, which ConfigureTopologySilo runs after this.
        silo.Services.AddKeyedSingleton<IGrainStorage>(_options.ClusterGrainStorage, new UnregisteredClusterStorage());
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, TopologyContext context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);
        var account = CloudStorageAccount.Parse(context.Configuration[_options.CloudStorageKey], _options);
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
