namespace DigitalBrain.Behaviors;

internal sealed class HostedBehaviorExecutor(IBehaviorHostGateway host) : IBehaviorExecutor
{
    public async ValueTask<BehaviorExecutionOutcome> ExecuteAsync(
        BehaviorExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var outcome = await host
            .ExecuteAsync(
                new BehaviorHostExecuteCommand(
                    request.Metadata,
                    request.ArtifactHash,
                    request.Task,
                    request.Attempt,
                    request.TriggerTypeName,
                    request.TriggerPayload,
                    request.Capabilities,
                    request.UtcNow,
                    request.Worker),
                cancellationToken)
            .ConfigureAwait(false);
        return RejectBareUserActionCode(outcome);
    }

    public async ValueTask<BehaviorExecutionOutcome> ExecuteLegacyAsync(
        LegacyBehaviorExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var outcome = await host.ExecuteLegacyAsync(request, cancellationToken).ConfigureAwait(false);
        return RejectBareUserActionCode(outcome);
    }

    private static BehaviorExecutionOutcome RejectBareUserActionCode(BehaviorExecutionOutcome outcome)
    {
        if (outcome.Succeeded
            || !string.Equals(outcome.Outcome, BehaviorExecutionCodes.UserActionRequired, StringComparison.Ordinal))
        {
            return outcome;
        }

        if (outcome.UserAction is null)
        {
            // Hosted path must carry the issued opaque surface; a bare stable code is not a park.
            return new BehaviorExecutionOutcome(false, BehaviorExecutionCodes.Exception);
        }

        if (outcome.UserAction.Task != default
            && outcome.UserAction.Attempt.Value != Guid.Empty
            && outcome.UserAction.ActionReference.Id != Guid.Empty
            && outcome.UserAction.ActionEpoch != Guid.Empty
            && outcome.UserAction.Completer != default)
        {
            return outcome;
        }

        return new BehaviorExecutionOutcome(false, BehaviorExecutionCodes.Exception);
    }
}
