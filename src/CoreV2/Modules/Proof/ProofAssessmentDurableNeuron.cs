using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;
using Brain.Core.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace Brain.Modules.Proof;

public sealed class ProofAssessmentDurableNeuron(
    [FromKeyedServices("received-firings")] IDurableDictionary<Guid, string> received,
    IGrainFactory grains)
    : DurableGrain, IProofAssessmentNeuron
{
    public async Task<string> ReceiveAsync(ProofDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (received.TryGetValue(delivery.FiringId, out var previous))
        {
            return previous;
        }

        const string result = "assessment";
        received.Add(delivery.FiringId, result);
        await WriteStateAsync();

        var activity = grains.GetGrain<IBrainActivityGrain>(
            $"{delivery.Invocation.WorkspaceId}/{delivery.ActivityId:n}");
        var context = new BrainOperationExecutionContext(
            delivery.ActivityId,
            delivery.Invocation,
            activity,
            grains);
        await context.JournalAsync(
            "proof-assessment-inbound",
            "proof/assessment/workspace",
            BrainJournalDirection.Inbound,
            "ProofProduced@1",
            "received",
            $"Assessment received '{delivery.Value}'",
            firingId: delivery.FiringId,
            causeFiringId: delivery.FiringId,
            synapseId: delivery.SynapseId,
            synapseRevision: delivery.SynapseRevision);
        await context.JournalAsync(
            "proof-assessed",
            "proof/assessment/workspace",
            BrainJournalDirection.Outbound,
            "ProofAssessed@1",
            "emitted",
            $"Proof assessed as {result}",
            firingId: context.DeterministicId("proof-assessed"),
            causeFiringId: delivery.FiringId);
        return result;
    }
}
