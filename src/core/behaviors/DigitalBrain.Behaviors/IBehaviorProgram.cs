namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;

public interface IBehaviorProgram<in TTrigger>
    where TTrigger : Synapse
{
    ValueTask ExecuteAsync(
        TTrigger trigger,
        IBehaviorContext context,
        CancellationToken cancellationToken);
}
