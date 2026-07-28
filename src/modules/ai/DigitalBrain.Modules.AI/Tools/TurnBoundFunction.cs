using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

internal sealed class TurnBoundFunction(AIFunction capability, TaskScheduler turnScheduler) : AIFunction
{
    public override string Name => capability.Name;

    public override string Description => capability.Description;

    public override JsonElement JsonSchema => capability.JsonSchema;

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
        => new(Task.Factory.StartNew(
            () => capability.InvokeAsync(arguments, cancellationToken).AsTask(),
            cancellationToken,
            TaskCreationOptions.DenyChildAttach,
            turnScheduler).Unwrap());
}
