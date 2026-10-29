using System.Reflection;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// Stream identity (04 D-06, D-12; RT-06): one stream per (brain, signal namespace) on the
/// <see cref="IntoChatOSNames.SignalStreams"/> binding. Namespace <c>brain.signals.&lt;signal namespace&gt;</c>, key = brain key.
/// Stream ids are persisted (PubSubStore, queues); they are pinned in ContractManifest.json.
/// </summary>
internal static class StreamNames
{
    /// <summary>The prefix of every signal stream namespace.</summary>
    public const string NamespacePrefix = "brain.signals.";

    /// <summary>The signal namespace of an alias: the alias up to its last dot, or the alias when it has none.</summary>
    public static string SignalNamespace(string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        var dot = alias.LastIndexOf('.');
        return dot <= 0 ? alias : alias[..dot];
    }

    /// <summary>The stream for one brain and the namespace of one signal alias.</summary>
    public static StreamId For(string brainKey, string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brainKey);
        return StreamId.Create(NamespacePrefix + SignalNamespace(alias), brainKey);
    }

    /// <summary>The stream for one brain and one signal namespace (already cut from an alias).</summary>
    public static StreamId ForNamespace(string brainKey, string signalNamespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brainKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(signalNamespace);
        return StreamId.Create(NamespacePrefix + signalNamespace, brainKey);
    }

    /// <summary>The stream for one brain and the namespace of a <c>[Signal]</c> type.</summary>
    /// <exception cref="ArgumentException">The type has no <see cref="SignalAttribute"/>.</exception>
    public static StreamId For(string brainKey, Type signalType)
    {
        ArgumentNullException.ThrowIfNull(signalType);
        var alias = signalType.GetCustomAttribute<SignalAttribute>()?.Alias
            ?? throw new ArgumentException($"{signalType.Name} has no [Signal] alias.", nameof(signalType));
        return For(brainKey, alias);
    }
}
