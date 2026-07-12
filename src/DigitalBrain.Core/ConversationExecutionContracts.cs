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
        if (!OAuthCallbackPaths.IsSupportedProvider(Provider) ||
            string.IsNullOrWhiteSpace(Invocation.ToolId) || Invocation.ToolId.Length > 256 ||
            Invocation.Input.ValueKind == JsonValueKind.Undefined ||
            !Guid.TryParseExact(AttemptId, "N", out _) || ExpiresAt == default)
            return false;
        var expectedTool = Provider == OAuthCallbackPaths.GoogleProvider
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
