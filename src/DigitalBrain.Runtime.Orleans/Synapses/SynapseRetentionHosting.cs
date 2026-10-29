using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>Registers the synapse cursor registry and the retention policy.</summary>
public static class SynapseRetentionHosting
{
    /// <summary>Binds <see cref="RetentionOptions"/> and registers the cursor lookup and <see cref="IRetentionPolicy"/>. The registry grains persist in the <c>cluster</c> store.</summary>
    public static ISiloBuilder AddSynapseRetention(this ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        AddRetentionOptions(silo.Services);
        silo.Services.TryAddSingleton(TimeProvider.System);
        silo.Services.AddSingleton<ISynapseCursorLookup, GrainSynapseCursorLookup>();
        silo.Services.AddSingleton<IRetentionPolicy, RetentionPolicy>();
        return silo;
    }

    internal static IServiceCollection AddRetentionOptions(IServiceCollection services)
    {
        services.AddOptions<RetentionOptions>().BindConfiguration(IntoChatOSNames.Signalling.Retention);
        return services;
    }
}
