using System.Text;
using System.Text.Json;

namespace DigitalBrain.Core.Runtime;

/// <summary>Application-owned execution port for durable commands.</summary>
public interface ICommandHandler
{
    bool CanHandle(string commandType);
    Task<CommandExecutionResult> ExecuteAsync(CommandEnvelope command, CancellationToken cancellationToken = default);
    Task<CommandExecutionResult> ExecuteAsync(
        CommandExecutionAttempt attempt,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(attempt.Command, cancellationToken);
}

public sealed record ExternalAuthorizationContinuation(
    string Provider,
    ToolInvocation Invocation,
    string AttemptId,
    DateTimeOffset ExpiresAt)
{
    public static ExternalAuthorizationContinuation Create(ExternalAuthorizationRequest request) =>
        new(
            request.Provider,
            new ToolInvocation(request.Invocation.ToolId, request.Invocation.Input.Clone()),
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow.AddMinutes(10));

    public ExternalAuthorizationContinuation Copy() =>
        this with { Invocation = Invocation with { Input = Invocation.Input.Clone() } };

    public bool IsValid()
    {
        if (Provider is not ("google" or "salesforce") ||
            string.IsNullOrWhiteSpace(Invocation.ToolId) || Invocation.ToolId.Length > 256 ||
            Invocation.Input.ValueKind == JsonValueKind.Undefined ||
            !Guid.TryParseExact(AttemptId, "N", out _) || ExpiresAt == default)
            return false;
        var expectedTool = Provider == "google"
            ? Invocation.ToolId.StartsWith("gmail.", StringComparison.Ordinal) ||
              Invocation.ToolId.StartsWith("cross.", StringComparison.Ordinal)
            : Invocation.ToolId.StartsWith("salesforce.", StringComparison.Ordinal) ||
              Invocation.ToolId.StartsWith("cross.", StringComparison.Ordinal);
        return expectedTool && Encoding.UTF8.GetByteCount(Invocation.Input.GetRawText()) <= 64 * 1024;
    }

    public bool Matches(ExternalAuthorizationContinuation other) =>
        string.Equals(Provider, other.Provider, StringComparison.Ordinal) &&
        string.Equals(Invocation.ToolId, other.Invocation.ToolId, StringComparison.Ordinal) &&
        string.Equals(Invocation.Input.GetRawText(), other.Invocation.Input.GetRawText(), StringComparison.Ordinal) &&
        string.Equals(AttemptId, other.AttemptId, StringComparison.Ordinal) &&
        ExpiresAt == other.ExpiresAt;
}

public sealed record ExternalAuthorizationWait(
    string OperationId,
    CommandEnvelope Command,
    ExternalAuthorizationContinuation Continuation);

public enum ExternalAuthorizationResolutionState { Waiting, Ready, Failed }

[GenerateSerializer, Alias("digitalbrain.v2.external-authorization-resolution")]
public sealed record ExternalAuthorizationResolution(
    [property: Id(0)] ExternalAuthorizationResolutionState State,
    [property: Id(1)] string? SafeReason = null);

public sealed record CommandExecutionAttempt(
    CommandEnvelope Command,
    ExternalAuthorizationContinuation? Authorization = null,
    ExternalAuthorizationResolution? AuthorizationResolution = null);

public sealed record CommandExecutionResult(
    WorkflowState State,
    string? SafeReason = null,
    ExternalAuthorizationContinuation? Authorization = null)
{
    public static CommandExecutionResult Success() => new(WorkflowState.Succeeded);
    public static CommandExecutionResult Unknown(string reason) => new(WorkflowState.OutcomeUnknown, reason);
    public static CommandExecutionResult AwaitAuthorization(ExternalAuthorizationContinuation continuation) =>
        new(
            WorkflowState.AwaitingExternalAuthorization,
            Authorization: continuation.Copy());
}

/// <summary>Claims and executes one operation without allowing a second worker to duplicate it.</summary>
public sealed class CommandDispatcher(ApplicationService application, IEnumerable<ICommandHandler> handlers)
{
    private readonly IReadOnlyList<ICommandHandler> _handlers = handlers.ToArray();

    public async Task<bool> DispatchAsync(string operationId, CancellationToken cancellationToken = default)
    {
        if (!application.TryClaimPending(
                operationId,
                out var command,
                out var authorization,
                out var authorizationResolution) || command is null) return false;
        var handler = _handlers.FirstOrDefault(x => x.CanHandle(command.Type));
        if (handler is null)
        {
            application.RecordOutcome(operationId, WorkflowState.ManualIntervention, "No command handler is registered.");
            return true;
        }

        try
        {
            var result = await handler.ExecuteAsync(
                new CommandExecutionAttempt(command, authorization, authorizationResolution),
                cancellationToken).ConfigureAwait(false);
            application.RecordOutcome(operationId, result.State, result.SafeReason, result.Authorization);
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
