using DigitalBrain.Capabilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// Local plus a connection to a cloud server: the local bindings, an absolute https cloud server address (<see cref="RuntimeOptions.CloudServerAddressKey"/>),
/// and a loaded module that provides <c>circle@1</c> (the circle module itself arrives in Phase 10).
/// </summary>
public sealed class LocalCloudProfile : ITopologyProfile, IRuntimeOptionsProfile
{
    /// <summary>The capability a local+cloud host needs a module to provide.</summary>
    public static CapabilityId Circle { get; } = new("circle", 1);

    private readonly RuntimeOptions _options;
    private readonly LocalProfile _local;

    /// <summary>Creates the local+cloud topology.</summary>
    /// <param name="options">The runtime's key and store names; null means <see cref="RuntimeOptions.IntoChatCompatible"/>.</param>
    public LocalCloudProfile(RuntimeOptions? options = null)
    {
        _options = options ?? RuntimeOptions.IntoChatCompatible;
        _local = new LocalProfile(_options);
    }

    /// <inheritdoc />
    public RuntimeOptions Options => _options;

    /// <inheritdoc />
    public Topology Topology => Topology.LocalCloud;

    /// <inheritdoc />
    public Bindings Bindings => _local.Bindings;

    /// <inheritdoc />
    public Services Services => _local.Services;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string?> Defaults => _local.Defaults;

    /// <inheritdoc />
    public IEnumerable<TopologyIssue> Validate(IConfiguration configuration, IReadOnlyList<ModuleManifest> modules)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(modules);
        foreach (var issue in _local.Validate(configuration, modules)) { yield return issue; }

        var address = configuration[_options.CloudServerAddressKey];
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            yield return new TopologyIssue(_options.CloudServerAddressKey, "Topology=local+cloud requires an absolute https server address.");
        }
        if (!modules.Any(module => module.Provides.Contains(Circle)))
        {
            yield return new TopologyIssue("modules", $"Topology=local+cloud requires a loaded module that provides {Circle}.");
        }
    }

    /// <inheritdoc />
    public void ConfigureConfiguration(IConfigurationBuilder builder, TopologyContext context) => _local.ConfigureConfiguration(builder, context);

    /// <inheritdoc />
    public void ConfigureSilo(ISiloBuilder silo, TopologyContext context) => _local.ConfigureSilo(silo, context);

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, TopologyContext context) => _local.ConfigureServices(services, context);
}
