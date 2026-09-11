using DigitalBrain.Abstractions.Neurons;
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
        // A permanent rejection is advice for the model.
        catch (Exception error) when (error is not OperationCanceledException && !TransientFailure.Covers(error))
        {
            return $"error: {error.Message}";
        }
    }
}
