using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Synapses;
using Orleans.Journaling;

namespace DigitalBrain.Core;

internal sealed class NeuronSynapses(
    IDurableDictionary<string, Synapse> synapses,
    IDurableDictionary<string, string[]> owners,
    NeuronId source,
    TimeProvider clock)
{
    private const string Manual = "";
    private const int MaxOwnedEdges = 1024;
    private const int MaxOwnersPerEdge = 64;

    internal static void ValidateOwner(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (owner.Length > 256)
        {
            throw new ArgumentException("An ownership identity must be at most 256 characters.", nameof(owner));
        }
    }

    private static string KeyFor(NeuronId target, string signalType) => $"{target} {signalType}";

    internal IReadOnlyList<Synapse> All() => [.. synapses.Values];

    internal IReadOnlyList<Synapse> ForType(string signalType)
        => [.. synapses.Values.Where(s => string.Equals(s.SignalType, signalType, StringComparison.Ordinal))];

    // Idempotent: connecting twice is one synapse.
    internal bool Connect(NeuronId target, string signalType)
    {
        var key = KeyFor(target, signalType);
        if (owners.TryGetValue(key, out var leases) && !leases.Contains(Manual, StringComparer.Ordinal))
        {
            // Manual ownership is independent of the bounded behavior leases.
            owners[key] = [.. leases, Manual];
            return true;
        }

        if (synapses.ContainsKey(key))
        {
            return false;
        }

        synapses[key] = new Synapse(source, target, signalType, clock.GetUtcNow());
        return true;
    }

    // No-op when absent.
    internal bool Disconnect(NeuronId target, string signalType)
    {
        var key = KeyFor(target, signalType);
        return owners.ContainsKey(key) ? DisconnectFor(Manual, target, signalType) : synapses.Remove(key);
    }

    internal bool ConnectFor(string owner, NeuronId target, string signalType)
    {
        var key = KeyFor(target, signalType);
        if (!owners.TryGetValue(key, out var leases))
        {
            if (owners.Count >= MaxOwnedEdges)
            {
                throw new InvalidOperationException($"Neuron '{source}' already has {MaxOwnedEdges} owned connections.");
            }

            // Edges written before lease metadata existed belong to the manual caller.
            leases = synapses.ContainsKey(key) ? [Manual] : [];
        }

        if (leases.Contains(owner, StringComparer.Ordinal))
        {
            return false;
        }

        if (leases.Count(lease => lease != Manual) >= MaxOwnersPerEdge)
        {
            throw new InvalidOperationException($"Connection '{key}' already has {MaxOwnersPerEdge} owners.");
        }

        owners[key] = [.. leases, owner];
        if (!synapses.ContainsKey(key))
        {
            synapses[key] = new Synapse(source, target, signalType, clock.GetUtcNow());
        }

        return true;
    }

    internal bool DisconnectFor(string owner, NeuronId target, string signalType)
    {
        var key = KeyFor(target, signalType);
        if (!owners.TryGetValue(key, out var leases) || !leases.Contains(owner, StringComparer.Ordinal))
        {
            return false;
        }

        var remaining = leases.Where(lease => !string.Equals(lease, owner, StringComparison.Ordinal)).ToArray();
        if (remaining.Length == 0)
        {
            owners.Remove(key);
            synapses.Remove(key);
        }
        else if (remaining.Length == 1 && remaining[0] == Manual)
        {
            // A manual-only edge needs no metadata; reclaim the bounded lease slot.
            owners.Remove(key);
        }
        else
        {
            owners[key] = remaining;
        }

        return true;
    }
}
