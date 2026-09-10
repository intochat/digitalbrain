using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

internal sealed class TurnBoundFunction(AIFunction capability, TaskScheduler turnScheduler) : DelegatingAIFunction(capability)
{
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Factory.StartNew(
                () => base.InvokeCoreAsync(arguments, cancellationToken).AsTask(),
                cancellationToken,
                TaskCreationOptions.DenyChildAttach,
                turnScheduler).Unwrap().ConfigureAwait(true);
        }
        // A rejection is advice, and the model is the one who must act on it.
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return $"error: {error.Message}";
        }
    }
}
