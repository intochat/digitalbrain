using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Capabilities.Connections.Broker;
using DigitalBrain.Capabilities;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Units;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// The slot resolver of the silo: a slot's binding in the install record of its alias is a connection key that the brain's
/// <see cref="ICapabilityConnections"/> lists as connected and providing the slot's capability; the connection is bound for the
/// alias as caller app and wrapped by every <see cref="ICapabilityBindingInterceptor"/>. Any miss fails closed with
/// <see cref="SlotUnboundException"/>.
/// </summary>
internal sealed class BrokerSlotResolver(IServiceProvider services) : ISlotResolver
{
    private static readonly MethodInfo BindOpen = typeof(BrokerSlotResolver).GetMethod(nameof(Bind), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly ConcurrentDictionary<Type, MethodInfo> BindClosed = new();

    /// <inheritdoc />
    public async Task<object> Resolve(NeuronId state, SlotDescriptor slot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(slot);
        var installs = NeuronHosting.Get<IInstalls>(services, new NeuronId(state.Brain, OrleansInstallHooks.InstallsType, "default"));
        // The slot is resolved for the unit, as the brain's platform: the activating caller may belong to another brain.
        InstallRecord? record;
        using (RuntimeCallerContext.StampScope(CapabilityCallers.Platform(state.Brain.Value, state.Brain.Value, state.Brain.Value)))
        { record = await installs.Describe(state.Type, ct).ConfigureAwait(true); }
        if (record is null || !record.Bindings.TryGetValue(slot.Name, out var key)) { throw new SlotUnboundException(state.Type, slot.Name); }
        var (connection, problem) = await Find(services, state, slot, key, ct).ConfigureAwait(true);
        if (problem is not null) { throw new SlotUnboundException(state.Type, slot.Name); }
        var bind = BindClosed.GetOrAdd(slot.Contract, contract => BindOpen.MakeGenericMethod(contract));
        return await ((Task<object>)bind.Invoke(this, [state, connection!, ct])!).ConfigureAwait(true);
    }

    /// <summary>The connection <paramref name="key"/> of the brain when it is connected and provides the slot's capability, or why not.</summary>
    internal static async Task<(BrainConnection? Connection, string? Problem)> Find(IServiceProvider services, NeuronId state, SlotDescriptor slot, string key, CancellationToken ct)
    {
        if (services.GetService<ICapabilityConnections>() is not { } connections) { return (null, $"slot '{slot.Name}': this host has no capability connections"); }
        var connection = (await connections.ListInOrder(state.Brain, ct).ConfigureAwait(true)).FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal));
        if (connection is null) { return (null, $"slot '{slot.Name}': no connection '{key}'"); }
        if (!connection.Connected) { return (null, $"slot '{slot.Name}': connection '{key}' is not connected"); }
        return connection.Provides.Any(provided => provided.Satisfies(slot.Capability))
            ? (connection, null)
            : (null, $"slot '{slot.Name}': connection '{key}' does not provide {slot.Capability}");
    }

    private async Task<object> Bind<TCapability>(NeuronId state, BrainConnection connection, CancellationToken ct) where TCapability : class
    {
        var implementation = await services.GetRequiredService<ICapabilityConnections>().Bind<TCapability>(state.Brain, connection.Key, state.Type, ct).ConfigureAwait(true);
        foreach (var interceptor in services.GetServices<ICapabilityBindingInterceptor>()) { implementation = interceptor.Wrap(state.Brain, connection, state.Type, implementation); }
        return implementation;
    }
}
