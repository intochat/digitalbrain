namespace DigitalBrain.Behaviors;

public interface IIntentProgram<TRequest, TResponse>
{
    ValueTask<TResponse> ExecuteAsync(
        TRequest request,
        IBehaviorContext context,
        CancellationToken cancellationToken);
}
