namespace DigitalBrain.Core.Runtime;

/// <summary>Application-owned execution port for durable commands.</summary>
public interface ICommandHandler
{
    bool CanHandle(string commandType);
    Task<CommandExecutionResult> ExecuteAsync(CommandEnvelope command, CancellationToken cancellationToken = default);
}

public sealed record CommandExecutionResult(WorkflowState State, string? SafeReason = null)
{
    public static CommandExecutionResult Success() => new(WorkflowState.Succeeded);
    public static CommandExecutionResult Unknown(string reason) => new(WorkflowState.OutcomeUnknown, reason);
}

/// <summary>Claims and executes one operation without allowing a second worker to duplicate it.</summary>
public sealed class CommandDispatcher(ApplicationService application, IEnumerable<ICommandHandler> handlers)
{
    private readonly IReadOnlyList<ICommandHandler> _handlers = handlers.ToArray();

    public async Task<bool> DispatchAsync(string operationId, CancellationToken cancellationToken = default)
    {
        if (!application.TryClaimPending(operationId, out var command) || command is null) return false;
        var handler = _handlers.FirstOrDefault(x => x.CanHandle(command.Type));
        if (handler is null)
        {
            application.RecordOutcome(operationId, WorkflowState.ManualIntervention, "No command handler is registered.");
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
            application.RecordOutcome(operationId, WorkflowState.OutcomeUnknown, "Command outcome could not be determined.");
        }
        return true;
    }
}
