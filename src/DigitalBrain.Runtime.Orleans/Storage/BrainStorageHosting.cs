using System.Reflection;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Synapses;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Hosting;
using Orleans.Storage;

using OrleansSdk = Orleans;
namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// Registers BrainLog as the <c>cluster</c> grain store (moved from os Authority in 04-05). Reached through the host-bound
/// seam: <c>builder.UseClusterStore(new BrainStorageRegistration(moduleAssemblies))</c>, so topologies never register
/// <c>cluster</c> themselves.
/// </summary>
public static class BrainStorageHosting
{
    /// <summary>
    /// Registers BrainLog under <c>&lt;Home&gt;/state</c>, its JSON payloads for the <c>[Signal]</c> records of
    /// <paramref name="signalAssemblies"/>, and <see cref="IBrainJournalWriter"/> over the same logs, and replaces any earlier keyed
    /// <c>cluster</c> store with it.
    /// </summary>
    public static ISiloBuilder AddBrainStorage(this ISiloBuilder silo, params Assembly[] signalAssemblies)
    {
        ArgumentNullException.ThrowIfNull(silo);
        AddBrainStorageServices(silo.Services, signalAssemblies);
        return silo;
    }

    /// <summary>
    /// Adds the assemblies whose <c>[Signal]</c> records the journal encodes and decodes (the composed module assemblies).
    /// May be called more than once; <see cref="Gap"/> is always included.
    /// </summary>
    public static IServiceCollection AddJournalPayloads(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);
        services.AddSingleton(new SignalAssemblies(assemblies));
        services.TryAddSingleton(sp => new SignalTypeMap(
            sp.GetServices<SignalAssemblies>()
                .SelectMany(item => item.Assemblies)
                .Concat(Capabilities.CapabilityHosting.RegisteredPortAssemblies(sp))
                .Distinct()));
        services.TryAddSingleton<JournalPayloads>();
        services.TryAddSingleton<BrainSignalHistories>();
        // The brain history the HTTP journal tail (DigitalBrain.Runtime.Http) resolves.
        services.TryAddSingleton<Func<BrainId, DigitalBrain.Spi.ISignalHistory>>(sp => sp.GetRequiredService<BrainSignalHistories>().For);
        return services;
    }

    internal static IServiceCollection AddBrainStorageServices(IServiceCollection services, Assembly[] signalAssemblies)
    {
        var runtime = RuntimeOptions.From(services);
        services.AddOptions<BrainStoreOptions>()
            .PostConfigure<IConfiguration>((options, configuration) => options.Directory = StateHome.Resolve(configuration, runtime).State)
            .Validate(options => !string.IsNullOrWhiteSpace(options.Directory), "Brain storage needs a directory.");
        services.AddJournalPayloads(signalAssemblies);
        SynapseRetentionHosting.AddRetentionOptions(services, runtime);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRetentionPolicy, DefaultRetentionPolicy>();
        services.TryAddSingleton<IBrainKeyCustody, ConfiguredBrainKeyCustody>();
        services.TryAddSingleton<IBrainLogOwnership>(FileLockBrainLogOwnership.Instance);
        services.TryAddSingleton<IBrainOwnershipCounters, BrainLogOwnershipCounters>();
        services.TryAddSingleton<BrainStorageKeys>();
        services.TryAddSingleton(sp =>
        {
            var logs = new BrainLogs(sp.GetRequiredService<BrainStorageKeys>(),
                sp.GetRequiredService<OrleansSdk.Serialization.Serializer>(), sp.GetRequiredService<JournalPayloads>(),
                sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IRetentionPolicy>(),
                sp.GetRequiredService<IOptions<BrainStoreOptions>>(), sp.GetRequiredService<IBrainLogOwnership>());
            logs.OnLogCreated = log =>
            {
                var types = sp.GetService<NeuronTypeRegistry>();
                var grains = sp.GetService<IGrainFactory>();
                var faults = sp.GetService<Synapses.PublishFaults>();
                var nudge = sp.GetService<DigitalBrain.Kernel.INudge>()
                    ?? (grains is not null && types is not null ? new Synapses.GrainNudge(grains, types, faults) : null);
                var listeners = sp.GetService<Signalling.BrainListeners>();
                if (nudge is not null)
                {
                    log.Fanout = new DigitalBrain.Kernel.Fanout(
                        log,
                        nudge,
                        alias => types?.TryGetRegistration(alias, out var r) == true ? r.Class : null,
                        (pub, sigs) =>
                        {
                            if (faults?.ShouldFail(pub) == true)
                            {
                                throw new InvalidOperationException("PublishFaults: FailAfterAppend.");
                            }
                            sp.GetService<Signalling.ClusterListeners>()?.Offer(pub, sigs);
                        });
                }
            };
            return logs;
        });
        // The adapters' write path shares the per-brain BrainLog (and its writer claim) with the cluster store: one writer per brain.
        services.TryAddSingleton<IBrainJournalWriter>(sp => new BrainJournalWriter(sp.GetRequiredService<BrainLogs>()));
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IGrainStorage) && Equals(d.ServiceKey, runtime.ClusterGrainStorage)).ToArray())
        { services.Remove(descriptor); }
        services.AddKeyedSingleton<IGrainStorage>(runtime.ClusterGrainStorage, (sp, _) => new BrainGrainStorage(sp.GetRequiredService<BrainLogs>()));
        return services;
    }
}

/// <summary>
/// The runtime's <see cref="IClusterStoreRegistration"/>: BrainLog as the <c>cluster</c> store. os passes it to
/// <c>UseClusterStore</c> (06-02) instead of its own Authority registration.
/// </summary>
/// <param name="signalAssemblies">The assemblies whose <c>[Signal]</c> records are journaled.</param>
public sealed class BrainStorageRegistration(params Assembly[] signalAssemblies) : IClusterStoreRegistration
{
    /// <inheritdoc />
    public void Register(ISiloBuilder silo) => silo.AddBrainStorage(signalAssemblies);
}
