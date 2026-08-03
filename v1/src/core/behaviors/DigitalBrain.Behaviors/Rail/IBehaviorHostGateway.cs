namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;

public interface IBehaviorHostGateway
{
    ValueTask DeployAsync(BehaviorHostDeployCommand command, CancellationToken cancellationToken);

    ValueTask ActivateAsync(BehaviorHostActivationCommand command, CancellationToken cancellationToken);

    ValueTask DeactivateAsync(BehaviorHostDeactivationCommand command, CancellationToken cancellationToken);

    ValueTask<BehaviorExecutionOutcome> ExecuteAsync(
        BehaviorHostExecuteCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// Process-local migration seam only; must never cross HTTP.
    /// </summary>
    ValueTask<BehaviorExecutionOutcome> ExecuteLegacyAsync(
        LegacyBehaviorExecutionRequest request,
        CancellationToken cancellationToken);
}
