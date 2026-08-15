using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Execution;

internal static class ExecutionDeliveryAuthorizer
{
    internal static bool ShouldDeliver(
        SynapseDelivery delivery,
        NeuronId execution,
        Func<ExecutionData?> loadIfStarted)
    {
        if (delivery.Synapse is AttemptFact fact && delivery.Caller != fact.Worker)
        {
            return false;
        }

        if (delivery.Synapse is UserActionRequired)
        {
            var data = loadIfStarted();
            if (data is null || delivery.Caller != data.Worker)
            {
                return false;
            }
        }

        if (delivery.Synapse is CompleteUserAction or DenyUserAction)
        {
            AuthorizeUserActionCompletion(delivery, execution, loadIfStarted());
        }

        if (delivery.Synapse is PrepareOperation or TransitionOperation)
        {
            var data = loadIfStarted();
            if (data is null || delivery.Caller != data.Worker)
            {
                throw new NeuronAuthorizationException(
                    $"Caller '{delivery.Caller}' is not authorized to submit operations for "
                    + $"Execution '{execution}'.");
            }
        }
        else if (delivery.Synapse is ReadOperation)
        {
            var data = loadIfStarted();
            if (data is null || !IsAuthorizedOperationReader(delivery.Caller, data.Worker, execution))
            {
                throw new NeuronAuthorizationException(
                    $"Caller '{delivery.Caller}' is not authorized to read operations for "
                    + $"Execution '{execution}'.");
            }
        }

        return true;
    }

    private static void AuthorizeUserActionCompletion(
        SynapseDelivery delivery,
        NeuronId execution,
        ExecutionData? data)
    {
        if (data is null)
        {
            throw NotUserActionCompleter(delivery.Caller, execution);
        }

        if (data.Blocker is not UserActionPending pending)
        {
            var expectedRevision = delivery.Synapse switch
            {
                CompleteUserAction complete => complete.ExpectedParkRevision,
                DenyUserAction deny => deny.ExpectedParkRevision,
                _ => -1L,
            };

            if (data.State is ExecutionState.Running or ExecutionState.Pending
                && expectedRevision >= 0
                && data.Revision == expectedRevision)
            {
                throw new InvalidOperationException(
                    $"Execution '{execution}' is not waiting on a module user action yet.");
            }

            throw NotUserActionCompleter(delivery.Caller, execution);
        }

        if (delivery.Caller != pending.Completer)
        {
            throw NotUserActionCompleter(delivery.Caller, execution);
        }
    }

    private static bool IsAuthorizedOperationReader(
        NeuronId caller,
        NeuronId worker,
        NeuronId execution)
        => caller == worker
            || (caller.Owner == execution.Owner
                && string.Equals(
                    caller.Type,
                    ISessionNeuron.GrainTypeName,
                    StringComparison.OrdinalIgnoreCase));

    private static NeuronAuthorizationException NotUserActionCompleter(
        NeuronId caller,
        NeuronId execution)
        => new($"Caller '{caller}' is not the user-action completer for Execution '{execution}'.");
}
