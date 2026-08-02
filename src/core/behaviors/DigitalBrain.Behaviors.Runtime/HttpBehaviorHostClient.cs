using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors.Runtime;

internal sealed class HttpBehaviorHostClient(HttpClient http) : IBehaviorHostGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async ValueTask DeployAsync(BehaviorHostDeployCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var response = await http.PostAsJsonAsync(
            "v1/behaviors/deploy",
            new DeployBody(
                command.Owner.Value,
                command.Behavior.Value,
                command.ArtifactHash,
                Convert.ToBase64String(command.ArtifactBytes.Span),
                Convert.ToBase64String(command.AssemblyBytes.Span),
                Convert.ToBase64String(command.Signature.Span)),
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ActivateAsync(BehaviorHostActivationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var response = await http.PostAsJsonAsync(
            "v1/behaviors/activate",
            new ActivationBody(command.Owner.Value, command.Behavior.Value, command.ArtifactHash),
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DeactivateAsync(BehaviorHostDeactivationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var response = await http.PostAsJsonAsync(
            "v1/behaviors/deactivate",
            new ActivationBody(command.Owner.Value, command.Behavior.Value, command.ArtifactHash),
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<BehaviorExecutionOutcome> ExecuteAsync(
        BehaviorHostExecuteCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var response = await http.PostAsJsonAsync(
            "v1/behaviors/execute",
            new ExecuteBody(
                command.Metadata.Owner.Value,
                command.Metadata.Behavior.Value,
                command.Metadata.Revision.Value,
                command.Metadata.Execution.Value.ToString("N"),
                command.ArtifactHash,
                command.TriggerTypeName,
                command.Task.Type,
                command.Task.Owner.Value,
                command.Task.Name,
                command.Attempt.Value.ToString("N"),
                command.TriggerPayload.Id.ToString("N"),
                command.TriggerPayload.ExpiresAt,
                command.Capabilities
                    .Select(static edge => new CapabilityEdgeBody(
                        edge.Target.Type,
                        edge.Target.Owner.Value,
                        edge.Target.Name,
                        edge.RequestSynapseId,
                        edge.RequestSchemaVersion,
                        edge.ResponseSynapseId,
                        edge.ResponseSchemaVersion))
                    .ToArray(),
                command.UtcNow,
                command.Worker.Type,
                command.Worker.Owner.Value,
                command.Worker.Name),
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var body = await response.Content
            .ReadFromJsonAsync<ExecuteResultBody>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BehaviorHostException("empty-execute-response");
        BehaviorUserActionSurface? userAction = null;
        if (!body.Succeeded
            && string.Equals(body.Outcome, BehaviorExecutionCodes.UserActionRequired, StringComparison.Ordinal)
            && body.UserAction is { } surface)
        {
            userAction = ParseUserActionSurface(surface);
        }

        return new BehaviorExecutionOutcome(body.Succeeded, body.Outcome, userAction);
    }

    private static BehaviorUserActionSurface ParseUserActionSurface(ExecuteUserActionBody surface)
    {
        if (string.IsNullOrWhiteSpace(surface.TaskType)
            || string.IsNullOrWhiteSpace(surface.TaskOwner)
            || string.IsNullOrWhiteSpace(surface.TaskName)
            || string.IsNullOrWhiteSpace(surface.Attempt)
            || string.IsNullOrWhiteSpace(surface.ModuleType)
            || string.IsNullOrWhiteSpace(surface.ModuleOwner)
            || string.IsNullOrWhiteSpace(surface.ModuleName)
            || string.IsNullOrWhiteSpace(surface.ModuleId)
            || string.IsNullOrWhiteSpace(surface.DisplayText)
            || string.IsNullOrWhiteSpace(surface.ActionReferenceId)
            || string.IsNullOrWhiteSpace(surface.ActionEpoch)
            || string.IsNullOrWhiteSpace(surface.CompleterType)
            || string.IsNullOrWhiteSpace(surface.CompleterOwner)
            || string.IsNullOrWhiteSpace(surface.CompleterName)
            || !Guid.TryParseExact(surface.Attempt, "N", out var attemptValue)
            || attemptValue == Guid.Empty
            || !Guid.TryParseExact(surface.ActionReferenceId, "N", out var referenceValue)
            || referenceValue == Guid.Empty
            || !Guid.TryParseExact(surface.ActionEpoch, "N", out var epochValue)
            || epochValue == Guid.Empty)
        {
            throw new BehaviorHostException("invalid-user-action-response");
        }

        return new BehaviorUserActionSurface(
            new NeuronId(surface.TaskType, new OwnerId(surface.TaskOwner), surface.TaskName),
            new AttemptId(attemptValue),
            new NeuronId(surface.ModuleType, new OwnerId(surface.ModuleOwner), surface.ModuleName),
            surface.ModuleId,
            surface.DisplayText,
            new ProtectedPayloadReference(referenceValue, surface.ActionReferenceExpiresAt),
            epochValue,
            surface.ParkRevision,
            surface.ExpiresAt,
            new NeuronId(surface.CompleterType, new OwnerId(surface.CompleterOwner), surface.CompleterName));
    }

    public ValueTask<BehaviorExecutionOutcome> ExecuteLegacyAsync(
        LegacyBehaviorExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException<BehaviorExecutionOutcome>(
            new BehaviorHostException("legacy-execution-not-transportable"));
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var reason = string.IsNullOrWhiteSpace(payload) ? $"http-{(int)response.StatusCode}" : payload.Trim();
        throw new BehaviorHostException(reason);
    }

    private sealed record DeployBody(
        string Owner,
        string Behavior,
        string ArtifactHash,
        string ArtifactBytesBase64,
        string AssemblyBytesBase64,
        string SignatureBase64);

    private sealed record ActivationBody(string Owner, string Behavior, string ArtifactHash);

    private sealed record ExecuteBody(
        string Owner,
        string Behavior,
        string Revision,
        string Execution,
        string ArtifactHash,
        string TriggerTypeName,
        string TaskType,
        string TaskOwner,
        string TaskName,
        string Attempt,
        string TriggerPayloadId,
        DateTimeOffset? TriggerPayloadExpiresAt,
        CapabilityEdgeBody[] Capabilities,
        DateTimeOffset UtcNow,
        string WorkerType,
        string WorkerOwner,
        string WorkerName);

    private sealed record CapabilityEdgeBody(
        string TargetType,
        string TargetOwner,
        string TargetName,
        string RequestId,
        int RequestVersion,
        string ResponseId,
        int ResponseVersion);

    private sealed record ExecuteResultBody(
        bool Succeeded,
        string Outcome,
        ExecuteUserActionBody? UserAction = null);

    private sealed record ExecuteUserActionBody(
        string? TaskType,
        string? TaskOwner,
        string? TaskName,
        string? Attempt,
        string? ModuleType,
        string? ModuleOwner,
        string? ModuleName,
        string? ModuleId,
        string? DisplayText,
        string? ActionReferenceId,
        DateTimeOffset? ActionReferenceExpiresAt,
        string? ActionEpoch,
        long ParkRevision,
        DateTimeOffset ExpiresAt,
        string? CompleterType,
        string? CompleterOwner,
        string? CompleterName);
}
