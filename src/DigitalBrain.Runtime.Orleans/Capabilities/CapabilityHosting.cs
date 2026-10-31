using DigitalBrain.Capabilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Capabilities;

/// <summary>Host configuration of the capability registry, read when the silo is configured.</summary>
public static class CapabilityHosting
{
    /// <summary>
    /// Configures the one <see cref="CapabilityRegistry"/> of this host (for example <c>AcceptBinding(storage@1, typeof(IStorage))</c>).
    /// Call before the silo is configured; module validation runs there.
    /// </summary>
    public static IHostApplicationBuilder ConfigureCapabilities(this IHostApplicationBuilder builder, Action<CapabilityRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        configure(Registry(builder.Services));
        return builder;
    }

    /// <summary>The registry instance in <paramref name="services"/>, added when absent.</summary>
    internal static CapabilityRegistry Registry(IServiceCollection services)
    {
        if (services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(CapabilityRegistry) && !descriptor.IsKeyedService)?.ImplementationInstance
            is CapabilityRegistry existing)
        {
            return existing;
        }
        var registry = new CapabilityRegistry();
        services.AddSingleton(registry);
        return registry;
    }

    /// <summary>Registers the broker over the host's <see cref="ICapabilityConnections"/> and the single-registration startup check.</summary>
    internal static void AddCapabilityBroker(this IServiceCollection services)
    {
        var registry = Registry(services);
        services.TryAddSingleton<DigitalBrain.Runtime.Orleans.Capabilities.Storage.StorageAccessLog>();
        services.TryAddSingleton(sp => new CapabilityBroker(
            registry,
            sp.GetService<ICapabilityConnections>()
                ?? throw new InvalidOperationException($"The host registers no {nameof(ICapabilityConnections)}; the capability broker needs one (the os kernel implements it)."),
            sp,
            sp.GetServices<ICapabilityBindingInterceptor>()));
        services.TryAddSingleton<ICapabilityBroker>(sp => sp.GetRequiredService<CapabilityBroker>());
        services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(new SingleCapabilityCheck(registry, services));
    }

    // Fails silo startup when a single-connection capability is registered twice; reads the collection at start, after host registrations.
    private sealed class SingleCapabilityCheck(CapabilityRegistry registry, IServiceCollection services) : ILifecycleParticipant<ISiloLifecycle>
    {
        public void Participate(ISiloLifecycle lifecycle)
            => lifecycle.Subscribe(nameof(SingleCapabilityCheck), ServiceLifecycleStage.First, _ =>
            {
                var issues = CapabilityBroker.SingleRegistrationIssues(registry, services);
                return issues.Count == 0 ? Task.CompletedTask : throw new InvalidOperationException(string.Join(Environment.NewLine, issues));
            });
    }
}
