using System.Text.Json;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;
using Brain.Core.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace Brain.Modules.Proof;

public sealed class ProofSourceDurableNeuron(
    [FromKeyedServices("processed-activities")] IDurableDictionary<Guid, string> processed,
    IGrainFactory grains)
    : DurableGrain, IProofSourceNeuron
{
    public async Task<string> RunAsync(ProofNeuronRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (processed.TryGetValue(request.ActivityId, out var previous))
        {
            return previous;
        }

        var activity = grains.GetGrain<IBrainActivityGrain>(
            $"{request.Invocation.WorkspaceId}/{request.ActivityId:n}");
        var context = new BrainOperationExecutionContext(
            request.ActivityId,
            request.Invocation,
            activity,
            grains);
        await context.JournalAsync(
            "proof-source-inbound",
            "proof/source/workspace",
            BrainJournalDirection.Inbound,
            "Proof.RunInput@1",
            "received",
            $"Proof source received '{request.Value}'");

        var graph = grains.GetGrain<IBrainGraphGrain>(request.Invocation.WorkspaceId);
        var snapshot = await graph.SnapshotAsync(request.Invocation.WorkspaceId);
        var routes = snapshot.Synapses
            .Where(synapse => string.Equals(synapse.SourceNeuronId, "proof/source/workspace", StringComparison.Ordinal)
                && string.Equals(synapse.InputContractId, "ProofProduced@1", StringComparison.Ordinal))
            .ToArray();
        var firing = context.DeterministicId("proof-produced");
        await context.JournalAsync(
            "proof-produced",
            "proof/source/workspace",
            BrainJournalDirection.Outbound,
            "ProofProduced@1",
            "emitted",
            $"ProofProduced '{request.Value}'",
            routes.Length,
            firing);

        var route = "unrouted";
        foreach (var synapse in routes)
        {
            await graph.RecordUsageAsync(
                synapse.Id,
                request.Invocation.WorkspaceId,
                request.ActivityId);
            await context.JournalAsync(
                $"delivery:{synapse.Id:n}:{synapse.Revision}",
                "proof/source/workspace",
                BrainJournalDirection.Delivery,
                synapse.OutputContractId,
                "delivered",
                $"Delivered ProofProduced@1 to {synapse.TargetNeuronId}",
                firingId: firing,
                causeFiringId: firing,
                synapseId: synapse.Id,
                synapseRevision: synapse.Revision);
            route = await grains
                .GetGrain<IProofAssessmentNeuron>(
                    $"{request.Invocation.WorkspaceId}/{synapse.TargetNeuronId}")
                .ReceiveAsync(new ProofDelivery(
                    request.ActivityId,
                    request.Invocation,
                    firing,
                    synapse.Id,
                    synapse.Revision,
                    request.Value));
        }

        var result = JsonSerializer.Serialize(new { route });
        processed.Add(request.ActivityId, result);
        await WriteStateAsync();
        return result;
    }
}
