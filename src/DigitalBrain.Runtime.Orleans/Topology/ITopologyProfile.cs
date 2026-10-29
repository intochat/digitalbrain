using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>One topology in code: its bindings, its process shape, its checks and how it configures a host (DESIGN section 9).</summary>
public interface ITopologyProfile
{
    /// <summary>The topology this implementation serves.</summary>
    Topology Topology { get; }

    /// <summary>What each resource kind is bound to.</summary>
    Bindings Bindings { get; }

    /// <summary>The process shape.</summary>
    Services Services { get; }

    /// <summary>Configuration defaults, below <c>&lt;Home&gt;/settings.json</c> in precedence.</summary>
    IReadOnlyDictionary<string, string?> Defaults { get; }

    /// <summary>Topology-specific checks; names keys only. The runtime adds the module needs check.</summary>
    IEnumerable<TopologyIssue> Validate(IConfiguration configuration, IReadOnlyList<ModuleManifest> modules);

    /// <summary>Adds topology configuration.</summary>
    void ConfigureConfiguration(IConfigurationBuilder builder, TopologyContext context);

    /// <summary>Configures the silo.</summary>
    void ConfigureSilo(ISiloBuilder silo, TopologyContext context);

    /// <summary>Registers topology services.</summary>
    void ConfigureServices(IServiceCollection services, TopologyContext context);
}
