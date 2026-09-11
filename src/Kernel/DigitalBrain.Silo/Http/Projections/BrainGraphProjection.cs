using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Kernel;

internal static class BrainGraphProjection
{
    private const int RecentDeliveries = 12;
    private const int MaxNodes = 64;

    public static async Task<BrainGraphSnapshot> ReadAsync(
        IGrainFactory grains, NeuronId chat, NeuronId session, CancellationToken cancellationToken)
    {
        var ids = new HashSet<NeuronId> { chat, session };
        var reads = new Dictionary<NeuronId, NeuronRead>();
        var truncated = false;
        // Only roots expand the frontier. Targets are read, but never expanded.
        foreach (var root in new[] { chat, session }.Distinct())
        {
            var read = await ReadNeuronAsync(grains, root, cancellationToken);
            reads.Add(root, read);
            foreach (var synapse in read.Synapses)
            {
                if (ids.Contains(synapse.Target))
                {
                    continue;
                }

                if (ids.Count == MaxNodes)
                {
                    truncated = true;
                    continue;
                }

                ids.Add(synapse.Target);
            }
        }

        var targets = ids.Where(id => !reads.ContainsKey(id)).ToArray();
        var targetReads = await Task.WhenAll(targets.Select(id => ReadNeuronAsync(grains, id, cancellationToken)));
        for (var index = 0; index < targets.Length; index++)
        {
            reads.Add(targets[index], targetReads[index]);
        }

        var activity = reads.SelectMany(pair =>
            pair.Value.Incoming.Delta.Select(delivery => ProjectDelivery(pair.Key, "incoming", delivery))
                .Concat(pair.Value.Outgoing.Delta.Select(delivery => ProjectDelivery(pair.Key, "outgoing", delivery))))
            .OrderBy(item => item.Timestamp).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var nodes = reads.Select(pair => new BrainGraphNode(pair.Key.ToString(), pair.Key.Type, pair.Key.Name,
            pair.Key.Name, pair.Key == chat ? "root" : pair.Key == session ? "session" : "observed",
            pair.Value.Incoming.ResumeSequence, pair.Value.Outgoing.ResumeSequence,
            pair.Value.Incoming.Delta.Concat(pair.Value.Outgoing.Delta)
                .Max(delivery => (DateTimeOffset?)delivery.Timestamp))).ToArray();
        var synapses = reads.Values.SelectMany(read => read.Synapses)
            .Where(edge => ids.Contains(edge.Source) && ids.Contains(edge.Target))
            .DistinctBy(edge => (edge.Source, edge.Target, edge.SignalType))
            .Select(edge => new BrainGraphSynapse($"{edge.Source}|{edge.SignalType}|{edge.Target}",
                edge.Source.ToString(), edge.Target.ToString(), edge.SignalType, "subscription", true)).ToArray();
        var correlations = activity.GroupBy(item => item.CorrelationId)
            .Select(group => new BrainCorrelation(group.Key, group.Last().Summary, group.Max(item => item.Timestamp),
                group.Select(item => item.NeuronId).Distinct(StringComparer.Ordinal).ToArray(), group.Count()))
            .OrderByDescending(item => item.LastAt).ToArray();
        return new BrainGraphSnapshot(chat.ToString(), DateTimeOffset.UtcNow, truncated,
            nodes, synapses, activity, correlations);
    }

    private static BrainGraphActivity ProjectDelivery(NeuronId neuron, string direction, SignalDelivery delivery)
    {
        var body = JsonNode.Parse(delivery.Signal.Body) as JsonObject;
        var preview = body?.ToDictionary(pair => pair.Key, pair => pair.Value?.ToJsonString() ?? "null", StringComparer.Ordinal);
        return new BrainGraphActivity($"{neuron}|{direction}|{delivery.SignalId}", neuron.ToString(), direction,
            delivery.Sequence, delivery.Signal.Type, delivery.Timestamp, delivery.Source.ToString(),
            delivery.CorrelationId.ToString(), delivery.Signal.Type, preview);
    }

    private static async Task<NeuronRead> ReadNeuronAsync(
        IGrainFactory grains, NeuronId id, CancellationToken cancellationToken)
    {
        var neuron = grains.GetGrain<INeuron>(id.ToGrainId());
        var synapses = neuron.ReadSynapses().WaitAsync(cancellationToken);
        var incoming = ReadRecentAsync(neuron, JournalKind.Incoming, cancellationToken);
        var outgoing = ReadRecentAsync(neuron, JournalKind.Outgoing, cancellationToken);
        await Task.WhenAll(synapses, incoming, outgoing);
        return new NeuronRead(await synapses, await incoming, await outgoing);
    }

    private static async Task<JournalRead> ReadRecentAsync(
        INeuron neuron, JournalKind kind, CancellationToken cancellationToken)
    {
        var head = await neuron.ReadJournal(kind, long.MaxValue).WaitAsync(cancellationToken);
        var cursor = Math.Max(Math.Max(0, head.ResumeSequence - RecentDeliveries), head.EarliestRetained - 1);
        var read = await neuron.ReadJournal(kind, cursor).WaitAsync(cancellationToken);
        if (read.Gap)
        {
            read = await neuron.ReadJournal(kind,
                Math.Max(Math.Max(0, read.ResumeSequence - RecentDeliveries), read.EarliestRetained - 1))
                .WaitAsync(cancellationToken);
        }

        // Concurrent appends can make the second read longer than the requested tail.
        return read with { Delta = read.Delta.TakeLast(RecentDeliveries).ToArray() };
    }

    private sealed record NeuronRead(IReadOnlyList<Synapse> Synapses, JournalRead Incoming, JournalRead Outgoing);
}

internal sealed record BrainGraphSnapshot(
    string RootId,
    DateTimeOffset ObservedAt,
    bool Truncated,
    IReadOnlyList<BrainGraphNode> Nodes,
    IReadOnlyList<BrainGraphSynapse> Synapses,
    IReadOnlyList<BrainGraphActivity> Activity,
    IReadOnlyList<BrainCorrelation> Correlations);

internal sealed record BrainGraphNode(
    string Id,
    string Type,
    string Name,
    string Label,
    string Role,
    long IncomingSequence,
    long OutgoingSequence,
    DateTimeOffset? LastActivityAt);

internal sealed record BrainGraphSynapse(
    string Id,
    string SourceId,
    string TargetId,
    string SignalType,
    string Kind,
    bool CanUnsubscribe);

internal sealed record BrainGraphActivity(
    string Id,
    string NeuronId,
    string Direction,
    long Sequence,
    string SignalType,
    DateTimeOffset? Timestamp,
    string CallerId,
    string CorrelationId,
    string Summary,
    IReadOnlyDictionary<string, string>? PayloadPreview);

internal sealed record BrainCorrelation(
    string CorrelationId,
    string Summary,
    DateTimeOffset? LastAt,
    IReadOnlyList<string> NeuronIds,
    int DeliveryCount);

internal sealed record BrainGraphSubscriptionRequest(string SourceId, string TargetId, string SignalType, bool Subscribed);

internal sealed record BrainGraphSubscriptionResult(string SourceId, string TargetId, string SignalType, bool Subscribed);
