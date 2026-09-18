namespace DigitalBrain.Abstractions.Behavior;

public interface IBehaviorRunLifecycle
{
    Task FinishedAsync(BehaviorDefinition definition, BehaviorRunSnapshot run, CancellationToken cancellationToken);
}
