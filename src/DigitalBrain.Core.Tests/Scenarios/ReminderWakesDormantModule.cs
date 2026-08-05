namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record ArmContractReview(string ContractId, TimeSpan DueIn) : Synapse;

public sealed record ContractReviewDue(string ContractId) : Synapse;

public sealed record ContractReminderSurfaced(string ContractId) : Synapse;

// Schedule survives deactivation; on due, self-tick lands as ordinary heard turn.
public sealed class ContractReview : Neuron, INeuron<ArmContractReview>, INeuron<ContractReviewDue>
{
    public Task HandleAsync(ArmContractReview fact, CancellationToken cancellationToken)
    {
        Schedule(new ContractReviewDue(fact.ContractId), fact.DueIn);
        return Task.CompletedTask;
    }

    public Task HandleAsync(ContractReviewDue fact, CancellationToken cancellationToken)
    {
        Emit(new ContractReminderSurfaced(fact.ContractId));
        Unschedule<ContractReviewDue>();
        return Task.CompletedTask;
    }
}

public sealed class ReminderSurfaceLedger : Neuron, INeuron<ContractReminderSurfaced>
{
    public Task HandleAsync(ContractReminderSurfaced fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
