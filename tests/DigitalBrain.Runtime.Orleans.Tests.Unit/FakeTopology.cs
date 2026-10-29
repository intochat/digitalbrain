using DigitalBrain.Capabilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit;

internal sealed class FakeTopology(Topology topology, Bindings? bindings = null, IReadOnlyDictionary<string, string?>? defaults = null,
    params TopologyIssue[] issues) : ITopologyProfile
{
    public Topology Topology { get; } = topology;
    public Bindings Bindings { get; } = bindings ?? AllBound;
    public Services Services => Services.Empty;
    public IReadOnlyDictionary<string, string?> Defaults { get; } = defaults ?? new Dictionary<string, string?>();
    public bool ServicesConfigured { get; private set; }
    public bool ConfigurationConfigured { get; private set; }

    public static Bindings AllBound { get; } = new(ClusteringBinding.Localhost, GrainStoreBinding.File, StreamsBinding.MemoryWithFilePubSub,
        RemindersBinding.File, SecretsBinding.MachineKey, HttpBinding.Direct, SandboxBinding.LocalContainer, ModelBinding.ConfiguredEndpoint, UiBinding.LocalWeb);

    public IEnumerable<TopologyIssue> Validate(IConfiguration configuration, IReadOnlyList<ModuleManifest> modules) => issues;

    public void ConfigureConfiguration(IConfigurationBuilder builder, TopologyContext context) => ConfigurationConfigured = true;

    public void ConfigureSilo(ISiloBuilder silo, TopologyContext context) { }

    public void ConfigureServices(IServiceCollection services, TopologyContext context) => ServicesConfigured = true;
}

internal static class Manifests
{
    internal static ModuleManifest Needing(string id, params Need[] needs)
        => new ModuleManifest(Array.Empty<CapabilityId>(), Array.Empty<CapabilityId>(), needs, [], []) { Id = id };
}
