using DigitalBrain.Neurons;
using System.Reflection;
using DigitalBrain.Brains;

namespace DigitalBrain.Contracts;

/// <summary>
/// A migrated contract is a plain interface that names the <see cref="NeuronAttribute"/> type it resolves to with
/// <see cref="DefaultNeuronTypeAttribute"/>. It does not derive <c>INeuron</c>, so the surfaces that list and call
/// contracts recognise it by that attribute.
/// </summary>
public static class PlainNeuronContracts
{
    public static bool Is(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.IsInterface
            && (type.IsDefined(typeof(DefaultNeuronTypeAttribute), inherit: false)
                || IsConnection(type)
                || type == typeof(IScreen)
                || type == typeof(Units.IViewReader));
    }

    // A contract every connection neuron may implement (IConnection, IStorage): it names no neuron type, the
    // connection key "{brain}/connection/{connector}/{id}" does.
    public static bool IsConnection(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.IsInterface && (type.IsDefined(typeof(ConnectionContractAttribute), inherit: false)
            || string.Equals(type.GetCustomAttribute<NeuronAliasAttribute>()?.Alias, "storage", StringComparison.Ordinal));
    }

    // The connector a canonical connection key names, or null.
    public static string? ConnectorOf(string fullKey)
    {
        ArgumentNullException.ThrowIfNull(fullKey);
        return fullKey.Split('/') is [var brain, "connection", var connector, var id] && NeuronAddress.IsBrainId(brain) && connector.Length > 0 && id.Length > 0
            ? Uri.UnescapeDataString(connector) : null;
    }

    // The neuron type the contract resolves to, or null when it is not a plain contract.
    public static string? TypeOf(Type contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (contract == typeof(IScreen)) { return "screen"; }
        if (contract == typeof(Units.IViewReader)) { return "synapse.view"; }
        return Is(contract) ? contract.GetCustomAttribute<DefaultNeuronTypeAttribute>(inherit: false)?.Type : null;
    }

    // The runtime neuron id the full key names for contract.
    public static NeuronId IdOf(Type contract, string fullKey)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullKey);
        var type = TypeOf(contract)
            ?? (IsConnection(contract)
                ? ConnectorOf(fullKey) ?? throw new ArgumentException($"{contract.Name} addresses a connection: '{fullKey}' is not a connection key.", nameof(fullKey))
                : throw new ArgumentException($"{contract.Name} is not a plain neuron contract.", nameof(contract)));
        var brain = NeuronAddress.ScopeOf(fullKey) ?? throw new ArgumentException($"'{fullKey}' names no brain: a plain neuron key is {{brain}}/{{key}}.", nameof(fullKey));
        return new NeuronId(new BrainId(brain), type, fullKey[(brain.Length + 1)..]);
    }
}
