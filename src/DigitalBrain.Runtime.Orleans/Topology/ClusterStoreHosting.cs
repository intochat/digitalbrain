using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>Registers the keyed <c>cluster</c> grain store on the silo. The host supplies it when the topology binds it as host-bound.</summary>
public interface IClusterStoreRegistration
{
    /// <summary>Registers the <c>cluster</c> grain store; runs after the topology configured the silo.</summary>
    void Register(ISiloBuilder silo);
}

/// <summary>The host-bound <c>cluster</c> grain store hook (03-CONTEXT R4-10).</summary>
public static class ClusterStoreHosting
{
    /// <summary>The hook named in validation messages.</summary>
    public const string HookName = nameof(UseClusterStore) + " (or an " + nameof(IClusterStoreRegistration) + " service instance)";

    /// <summary>Supplies the <c>cluster</c> grain store. Call before <see cref="TopologyHosting.UseTopology"/>.</summary>
    public static IHostApplicationBuilder UseClusterStore(this IHostApplicationBuilder builder, IClusterStoreRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(registration);
        builder.Services.AddSingleton(registration);
        return builder;
    }

    /// <summary>Supplies the <c>cluster</c> grain store as a delegate. Call before <see cref="TopologyHosting.UseTopology"/>.</summary>
    public static IHostApplicationBuilder UseClusterStore(this IHostApplicationBuilder builder, Action<ISiloBuilder> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        return builder.UseClusterStore(new DelegateRegistration(register));
    }

    /// <summary>True when the host supplied a <c>cluster</c> grain store registration instance.</summary>
    public static bool HasClusterStore(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Registrations(services).Count > 0;
    }

    internal static IReadOnlyList<IClusterStoreRegistration> Registrations(IServiceCollection services)
        => [.. services.Where(d => d.ServiceType == typeof(IClusterStoreRegistration) && !d.IsKeyedService)
            .Select(d => d.ImplementationInstance).OfType<IClusterStoreRegistration>()];

    private sealed class DelegateRegistration(Action<ISiloBuilder> register) : IClusterStoreRegistration
    {
        public void Register(ISiloBuilder silo) => register(silo);
    }
}
