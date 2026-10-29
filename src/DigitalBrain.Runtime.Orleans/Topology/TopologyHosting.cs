using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>Selects, validates and applies one topology before the silo is built (P2-D-09, TOPO-01).</summary>
public static class TopologyHosting
{
    /// <summary>The package that carries the cloud topology.</summary>
    public const string CloudPackage = "DigitalBrain.Runtime.Orleans.Azure";

    // The single composition entry: local and local+cloud are built in; cloud arrives through the Azure package.
    private static readonly ITopologyProfile[] BuiltIn = [new LocalProfile(), new LocalCloudProfile()];

    /// <summary>
    /// Parses the topology, selects its implementation, validates settings, the implementation and every module need, and
    /// throws once listing all issues. Then fixes precedence: command line, environment variables, <c>&lt;Home&gt;/settings.json</c>,
    /// topology defaults, module (options class) defaults.
    /// </summary>
    /// <exception cref="InvalidOperationException">Any issue; names keys only.</exception>
    public static ITopologyProfile UseTopology(
        this IHostApplicationBuilder builder,
        IReadOnlyList<ModuleManifest> modules,
        IReadOnlyList<RetiredSetting> retired,
        params ITopologyProfile[] additional)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(retired);
        ArgumentNullException.ThrowIfNull(additional);
        var configuration = builder.Configuration;

        // Home comes from the command line, environment or code, never from settings.json itself.
        var home = TryResolveHome(configuration);
        if (home is not null)
        {
            var json = new JsonConfigurationSource { Path = home.SettingsFile, Optional = true, ReloadOnChange = false };
            json.ResolveFileProvider();
            configuration.Sources.Insert(0, json);
        }

        var settings = TopologySettings.Parse(configuration, retired);
        var issues = settings.Issues.ToList();
        ITopologyProfile? selected = null;
        // Select whenever the topology itself parsed, so settings and implementation issues are reported together.
        if (!settings.Issues.Any(issue => issue.Key == IntoChatOSNames.TopologyKey))
        {
            selected = additional.Concat(BuiltIn).FirstOrDefault(candidate => candidate.Topology == settings.Topology);
            if (selected is null)
            {
                issues.Add(new TopologyIssue(IntoChatOSNames.TopologyKey, settings.Topology == Topology.Cloud
                    ? $"Topology=cloud requires {CloudPackage}; reference it and pass its implementation."
                    : $"Topology={settings.Topology.ToConfigValue()} has no registered implementation."));
            }
        }

        if (selected is not null)
        {
            configuration.Sources.Insert(0, new MemoryConfigurationSource { InitialData = selected.Defaults });
            issues.AddRange(selected.Validate(configuration, modules));
            issues.AddRange(ValidateNeeds(selected, modules, ClusterStoreHosting.HasClusterStore(builder.Services)));
        }

        TopologyValidator.ThrowIfInvalid(issues);

        var context = TopologyContext.Create(settings.Topology, settings.Home!, configuration, modules);
        selected!.ConfigureConfiguration(configuration, context);
        selected.ConfigureServices(builder.Services, context);
        builder.Services.AddSingleton(selected);
        builder.Services.AddSingleton(context);
        builder.Services.AddSingleton(settings);
        builder.Services.AddHostedService<TopologyWarningLog>();
        return selected;
    }

    /// <summary>
    /// <see cref="UseTopology(IHostApplicationBuilder, IReadOnlyList{ModuleManifest}, IReadOnlyList{RetiredSetting}, ITopologyProfile[])"/>
    /// for module classes: reads each manifest with <see cref="ModuleManifest.Of(Type)"/> and hosts the plain neurons, signals and
    /// stores of the module assemblies.
    /// </summary>
    /// <exception cref="InvalidOperationException">Any issue; names keys only.</exception>
    public static ITopologyProfile UseModuleTopology(
        this IHostApplicationBuilder builder,
        IReadOnlyList<Type> modules,
        IReadOnlyList<RetiredSetting> retired,
        params ITopologyProfile[] additional)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(modules);
        var selected = builder.UseTopology([.. modules.Select(ModuleManifest.Of)], retired, additional);
        builder.Services.AddSingleton(new ModuleAssemblies([.. modules.Select(module => module.Assembly).Distinct()], [.. modules]));
        return selected;
    }

    /// <summary>Every module need the selected bindings leave unbound (R4-02).</summary>
    public static IReadOnlyList<TopologyIssue> ValidateNeeds(ITopologyProfile topology, IReadOnlyList<ModuleManifest> modules)
    {
        ArgumentNullException.ThrowIfNull(topology);
        return TopologyValidator.ValidateNeeds(topology.Topology, topology.Bindings, modules);
    }

    /// <summary>Every module need the selected bindings leave unbound, given whether the host registered the <c>cluster</c> store.</summary>
    public static IReadOnlyList<TopologyIssue> ValidateNeeds(ITopologyProfile topology, IReadOnlyList<ModuleManifest> modules, bool clusterStoreRegistered)
    {
        ArgumentNullException.ThrowIfNull(topology);
        return TopologyValidator.ValidateNeeds(topology.Topology, topology.Bindings, modules, clusterStoreRegistered);
    }

    /// <summary>Applies the topology chosen by <see cref="UseTopology"/> to the silo.</summary>
    /// <exception cref="InvalidOperationException"><see cref="UseTopology"/> was not called on this host.</exception>
    public static ISiloBuilder ConfigureTopologySilo(this ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var selected = Instance<ITopologyProfile>(silo.Services);
        var context = Instance<TopologyContext>(silo.Services);
        selected.ConfigureSilo(silo, context);
        // 05-07: refuse unknown capabilities, Provides without an implementing neuron and two modules providing one single-connection capability.
        var moduleTypes = silo.Services.Select(d => d.ImplementationInstance).OfType<ModuleAssemblies>().SelectMany(m => m.Modules).Distinct().ToList();
        Capabilities.ModuleCapabilityValidator.Validate(Capabilities.CapabilityHosting.Registry(silo.Services), moduleTypes);
        // The one runtime composition call: every topology gets the host grains, journal payloads, publishing and retention.
        silo.AddDigitalBrainRuntime([.. silo.Services.Select(d => d.ImplementationInstance).OfType<ModuleAssemblies>().SelectMany(m => m.Assemblies).Distinct()]);
        // The host-bound cluster store goes last, so it replaces any cluster registration the topology made.
        foreach (var registration in ClusterStoreHosting.Registrations(silo.Services)) { registration.Register(silo); }
        silo.AddPlacementDirector<Placement.BrainAffinePlacement, Placement.BrainAffinePlacementDirector>();
        silo.Services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(sp => new PubSubStoreCheck(context.Topology, sp));
        return silo;
    }

    private static T Instance<T>(IServiceCollection services)
        => services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(T) && !descriptor.IsKeyedService)?.ImplementationInstance is T value
            ? value
            : throw new InvalidOperationException($"Call {nameof(UseTopology)} on the host builder before configuring the silo.");

    /// <summary>The declarations (privileged grants, secrets) of every module composed through <see cref="UseModuleTopology"/>.</summary>
    internal static ModuleDeclarations ComposedDeclarations(IServiceProvider services)
        => new([.. services.GetServices<ModuleAssemblies>().SelectMany(assemblies => assemblies.Modules).Distinct().Select(ModuleManifest.Of)]);

    private sealed record ModuleAssemblies(System.Reflection.Assembly[] Assemblies, Type[] Modules);

    private static StateHome? TryResolveHome(IConfiguration configuration)
    {
        try { return StateHome.Resolve(configuration); }
        catch (InvalidOperationException) { return null; } // reported by TopologySettings.Parse
    }
}
