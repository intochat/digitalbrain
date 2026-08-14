using Brain.Abstractions.Graph;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace Brain.Core.Graph;

public sealed class DurableBrainGraphGrain(
    [FromKeyedServices("neurons")] IDurableDictionary<string, BrainNeuronView> neurons,
    [FromKeyedServices("synapses")] IDurableDictionary<Guid, BrainSynapseView> synapses,
    [FromKeyedServices("synapse-history")] IDurableList<BrainSynapseView> history,
    [FromKeyedServices("graph-sequence")] IDurableValue<long> sequence)
    : DurableGrain, IBrainGraphGrain
{
    public async Task<BrainSynapseView> InstallAsync(BrainSynapseChange change)
    {
        ValidateWorkspace(change.WorkspaceId);
        var occupied = synapses.Values.Any(existing =>
            string.Equals(existing.Status, "live", StringComparison.Ordinal)
            && string.Equals(existing.SourceNeuronId, change.Source.Id, StringComparison.Ordinal)
            && string.Equals(existing.InputContractId, change.InputContractId, StringComparison.Ordinal));
        if (occupied)
        {
            throw new InvalidOperationException("A live Synapse already occupies the source contract route.");
        }

        var installed = ToView(Guid.NewGuid(), 1, change, "live", 0);
        neurons[change.Source.Id] = change.Source;
        neurons[change.Target.Id] = change.Target;
        synapses.Add(installed.Id, installed);
        history.Add(installed);
        sequence.Value++;
        await WriteStateAsync();
        return installed;
    }

    public async Task<BrainSynapseView> ReplaceAsync(Guid synapseId, BrainSynapseChange change)
    {
        ValidateWorkspace(change.WorkspaceId);
        var current = RequireSynapse(synapseId);
        if (!string.Equals(current.Status, "live", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only a live Synapse can be replaced.");
        }
        if (!string.Equals(current.SourceNeuronId, change.Source.Id, StringComparison.Ordinal)
            || !string.Equals(current.InputContractId, change.InputContractId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Replace cannot alter the stable source contract route.");
        }

        var replaced = ToView(synapseId, current.Revision + 1, change, "live", current.UsageCount);
        neurons[change.Source.Id] = change.Source with { FiringCount = neurons[change.Source.Id].FiringCount };
        neurons[change.Target.Id] = change.Target;
        synapses[synapseId] = replaced;
        history.Add(replaced);
        sequence.Value++;
        await WriteStateAsync();
        return replaced;
    }

    public async Task<BrainSynapseView> RetireAsync(Guid synapseId, string workspaceId, Guid activityId)
    {
        ValidateWorkspace(workspaceId);
        if (activityId == Guid.Empty)
        {
            throw new ArgumentException("A retirement activity is required.", nameof(activityId));
        }
        var current = RequireSynapse(synapseId);
        var retired = current with
        {
            Revision = current.Revision + 1,
            Status = "retired",
            ProvenanceActivityId = activityId,
        };
        synapses[synapseId] = retired;
        history.Add(retired);
        sequence.Value++;
        await WriteStateAsync();
        return retired;
    }

    public async Task RecordUsageAsync(Guid synapseId, string workspaceId, Guid activityId)
    {
        ValidateWorkspace(workspaceId);
        if (activityId == Guid.Empty)
        {
            throw new ArgumentException("A usage activity is required.", nameof(activityId));
        }
        var current = RequireSynapse(synapseId);
        if (!string.Equals(current.Status, "live", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A retired Synapse cannot record usage.");
        }

        synapses[synapseId] = current with { UsageCount = current.UsageCount + 1 };
        var source = neurons[current.SourceNeuronId];
        neurons[current.SourceNeuronId] = source with { FiringCount = source.FiringCount + 1 };
        sequence.Value++;
        await WriteStateAsync();
    }

    public Task<BrainSnapshot> SnapshotAsync(string workspaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        if (!string.Equals(this.GetPrimaryKeyString(), workspaceId, StringComparison.Ordinal))
        {
            return Task.FromResult(new BrainSnapshot(workspaceId, 0, TimeProvider.System.GetUtcNow(), [], []));
        }

        var liveSynapses = synapses.Values
            .Where(synapse => string.Equals(synapse.Status, "live", StringComparison.Ordinal))
            .OrderBy(synapse => synapse.SourceNeuronId, StringComparer.Ordinal)
            .ThenBy(synapse => synapse.InputContractId, StringComparer.Ordinal)
            .ToArray();
        var liveNeuronIds = liveSynapses
            .SelectMany(synapse => new[] { synapse.SourceNeuronId, synapse.TargetNeuronId })
            .ToHashSet(StringComparer.Ordinal);
        return Task.FromResult(new BrainSnapshot(
            workspaceId,
            sequence.Value,
            TimeProvider.System.GetUtcNow(),
            neurons.Values
                .Where(neuron => liveNeuronIds.Contains(neuron.Id))
                .OrderBy(neuron => neuron.Id, StringComparer.Ordinal)
                .ToArray(),
            liveSynapses));
    }

    public Task<IReadOnlyList<BrainSynapseView>> HistoryAsync(string workspaceId, Guid synapseId)
    {
        ValidateWorkspace(workspaceId);
        return Task.FromResult<IReadOnlyList<BrainSynapseView>>(
            history.Where(revision => revision.Id == synapseId).ToArray());
    }

    private void ValidateWorkspace(string workspaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        if (!string.Equals(this.GetPrimaryKeyString(), workspaceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The graph request does not match the addressed workspace.");
        }
    }

    private BrainSynapseView RequireSynapse(Guid synapseId)
    {
        if (synapseId == Guid.Empty || !synapses.TryGetValue(synapseId, out var synapse))
        {
            throw new KeyNotFoundException($"Synapse '{synapseId:n}' was not found.");
        }
        return synapse;
    }

    private static BrainSynapseView ToView(
        Guid id,
        long revision,
        BrainSynapseChange change,
        string status,
        long usageCount)
        => new(
            id,
            revision,
            change.Source.Id,
            change.Target.Id,
            change.InputContractId,
            change.OutputContractId,
            status,
            usageCount,
            change.ProvenanceActivityId);
}
