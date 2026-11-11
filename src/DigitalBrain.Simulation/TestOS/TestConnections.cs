using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Capabilities.Connections.Broker;
using DigitalBrain.Units;

namespace DigitalBrain.Testing;

// A slot binding a TestOS cannot match to a capability fake is looked up, as the silo's broker slot resolver does, in the
// ICapabilityConnections of TestOSOptions.Services: the connection must be listed, connected and provide the slot's capability.
internal static class TestConnections
{
    private static readonly MethodInfo BindOpen = typeof(TestConnections).GetMethod(nameof(Bind), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly ConcurrentDictionary<Type, MethodInfo> BindClosed = new();

    // Why the binding does not name a usable connection, or null when it does; null connections means no such host service.
    public static async Task<string?> Problem(IServiceProvider? services, NeuronId state, SlotDescriptor slot, string key, CancellationToken ct)
    {
        if (services?.GetService(typeof(ICapabilityConnections)) is not ICapabilityConnections connections)
        {
            return $"slot '{slot.Name}': no {slot.Capability} fake or connection named '{key}'";
        }

        var connection = (await connections.ListInOrder(state.Brain, ct).ConfigureAwait(false)).FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal));
        if (connection is null) { return $"slot '{slot.Name}': no connection '{key}'"; }
        if (!connection.Connected) { return $"slot '{slot.Name}': connection '{key}' is not connected"; }
        return connection.Provides.Any(provided => provided.Satisfies(slot.Capability)) ? null : $"slot '{slot.Name}': connection '{key}' does not provide {slot.Capability}";
    }

    public static async Task<object> Resolve(IServiceProvider services, NeuronId state, SlotDescriptor slot, string key, CancellationToken ct)
    {
        var connections = (ICapabilityConnections)services.GetService(typeof(ICapabilityConnections))!;
        var bind = BindClosed.GetOrAdd(slot.Contract, contract => BindOpen.MakeGenericMethod(contract));
        return await ((Task<object>)bind.Invoke(null, [connections, state, key, ct])!).ConfigureAwait(false);
    }

    private static async Task<object> Bind<TCapability>(ICapabilityConnections connections, NeuronId state, string key, CancellationToken ct) where TCapability : class
        => await connections.Bind<TCapability>(state.Brain, key, state.Type, ct).ConfigureAwait(false);
}
