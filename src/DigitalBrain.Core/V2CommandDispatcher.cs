namespace DigitalBrain.Core.V2;

/// <summary>Application-owned execution port for durable V2 commands.</summary>
public interface IV2CommandHandler
{
    bool CanHandle(string commandType);
    Task<V2CommandExecutionResult> ExecuteAsync(V2CommandEnvelope command, CancellationToken cancellationToken = default);
}

public sealed record V2CommandExecutionResult(WorkflowState State, string? SafeReason = null)
{
    public static V2CommandExecutionResult Success() => new(WorkflowState.Succeeded);
    public static V2CommandExecutionResult Unknown(string reason) => new(WorkflowState.OutcomeUnknown, reason);
}

/// <summary>Claims and executes one operation without allowing a second worker to duplicate it.</summary>
public sealed class V2CommandDispatcher(V2ApplicationService application, IEnumerable<IV2CommandHandler> handlers)
{
    private readonly IReadOnlyList<IV2CommandHandler> _handlers = handlers.ToArray();

    public async Task<bool> DispatchAsync(string operationId, CancellationToken cancellationToken = default)
    {
        if (!application.TryClaimPending(operationId, out var command) || command is null) return false;
        var handler = _handlers.FirstOrDefault(x => x.CanHandle(command.Type));
        if (handler is null)
        {
            application.RecordOutcome(operationId, WorkflowState.ManualIntervention, "No V2 command handler is registered.");
            return true;
        }

        try
        {
            var result = await handler.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            application.RecordOutcome(operationId, result.State, result.SafeReason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The effect may have crossed an external boundary. Never retry implicitly.
            application.RecordOutcome(operationId, WorkflowState.OutcomeUnknown, "V2 command outcome could not be determined.");
        }
        return true;
    }
}
