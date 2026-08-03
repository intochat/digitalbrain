using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Behaviors.Host;
using DigitalBrain.ServiceDefaults;
using DigitalBrain.Tasks;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddBehaviorHostEngine(builder.Configuration);

var app = builder.Build();
app.MapDefaultEndpoints();

app.MapPost("/v1/behaviors/deploy", async (
    DeployRequest body,
    BehaviorHostEngine host,
    CancellationToken cancellationToken) =>
{
    try
    {
        await host.DeployAsync(
            new BehaviorHostDeployCommand(
                RequireOwner(body.Owner, "missing-owner"),
                new BehaviorId(body.Behavior),
                body.ArtifactHash,
                Convert.FromBase64String(body.ArtifactBytesBase64),
                Convert.FromBase64String(body.AssemblyBytesBase64),
                Convert.FromBase64String(body.SignatureBase64)),
            cancellationToken);
        return Results.Ok();
    }
    catch (BehaviorHostException exception)
    {
        return Results.Content(exception.Reason, "text/plain", statusCode: StatusCodes.Status400BadRequest);
    }
});

app.MapPost("/v1/behaviors/activate", async (
    ActivationRequest body,
    BehaviorHostEngine host,
    CancellationToken cancellationToken) =>
{
    try
    {
        await host.ActivateAsync(
            new BehaviorHostActivationCommand(
                RequireOwner(body.Owner, "missing-owner"),
                new BehaviorId(body.Behavior),
                body.ArtifactHash),
            cancellationToken);
        return Results.Ok();
    }
    catch (BehaviorHostException exception)
    {
        return Results.Content(exception.Reason, "text/plain", statusCode: StatusCodes.Status400BadRequest);
    }
});

app.MapPost("/v1/behaviors/deactivate", async (
    ActivationRequest body,
    BehaviorHostEngine host,
    CancellationToken cancellationToken) =>
{
    try
    {
        await host.DeactivateAsync(
            new BehaviorHostDeactivationCommand(
                RequireOwner(body.Owner, "missing-owner"),
                new BehaviorId(body.Behavior),
                body.ArtifactHash),
            cancellationToken);
        return Results.Ok();
    }
    catch (BehaviorHostException exception)
    {
        return Results.Content(exception.Reason, "text/plain", statusCode: StatusCodes.Status400BadRequest);
    }
});

app.MapPost("/v1/behaviors/execute", async (
    ExecuteRequest body,
    BehaviorHostEngine host,
    CancellationToken cancellationToken) =>
{
    try
    {
        var owner = RequireOwner(body.Owner, "missing-owner");
        var taskOwner = RequireOwner(body.TaskOwner, "missing-task-owner");
        if (owner != taskOwner)
        {
            throw new BehaviorHostException("owner-task-mismatch");
        }

        if (string.IsNullOrWhiteSpace(body.TaskType)
            || string.IsNullOrWhiteSpace(body.TaskName)
            || string.IsNullOrWhiteSpace(body.Attempt)
            || string.IsNullOrWhiteSpace(body.TriggerPayloadId)
            || string.IsNullOrWhiteSpace(body.Execution)
            || body.Capabilities is null)
        {
            throw new BehaviorHostException("missing-execute-identity");
        }

        if (!Guid.TryParseExact(body.Execution, "N", out var executionValue)
            || executionValue == Guid.Empty)
        {
            throw new BehaviorHostException("invalid-execution");
        }

        if (!Guid.TryParseExact(body.Attempt, "N", out var attemptValue) || attemptValue == Guid.Empty)
        {
            throw new BehaviorHostException("invalid-attempt");
        }

        if (!Guid.TryParseExact(body.TriggerPayloadId, "N", out var payloadValue) || payloadValue == Guid.Empty)
        {
            throw new BehaviorHostException("invalid-trigger-payload");
        }

        if (string.IsNullOrWhiteSpace(body.WorkerType)
            || string.IsNullOrWhiteSpace(body.WorkerOwner)
            || string.IsNullOrWhiteSpace(body.WorkerName))
        {
            throw new BehaviorHostException("missing-worker-identity");
        }

        var workerOwner = RequireOwner(body.WorkerOwner, "missing-worker-identity");
        if (owner != workerOwner)
        {
            throw new BehaviorHostException("owner-task-mismatch");
        }

        var outcome = await host.ExecuteAsync(
            new BehaviorHostExecuteCommand(
                new BehaviorExecutionMetadata(
                    owner,
                    new BehaviorId(body.Behavior),
                    new BehaviorRevisionId(body.Revision),
                    new BehaviorExecutionId(executionValue)),
                body.ArtifactHash,
                new NeuronId(body.TaskType, taskOwner, body.TaskName),
                new AttemptId(attemptValue),
                body.TriggerTypeName,
                new ProtectedPayloadReference(payloadValue, body.TriggerPayloadExpiresAt),
                body.Capabilities
                    .Select(static edge => new BehaviorCapabilityEdge(
                        new NeuronId(
                            edge.TargetType,
                            RequireOwner(edge.TargetOwner, "missing-capability-owner"),
                            edge.TargetName),
                        edge.RequestId,
                        edge.RequestVersion,
                        edge.ResponseId,
                        edge.ResponseVersion))
                    .ToArray(),
                body.UtcNow,
                new NeuronId(body.WorkerType, workerOwner, body.WorkerName),
                ClampHops(body.Hops)),
            cancellationToken);
        var code = outcome.Succeeded
            ? BehaviorExecutionCodes.Succeeded
            : BehaviorExecutionCodes.MapHostFailure(outcome.Outcome);
        ExecuteUserActionResponse? userAction = null;
        if (!outcome.Succeeded
            && string.Equals(code, BehaviorExecutionCodes.UserActionRequired, StringComparison.Ordinal)
            && outcome.UserAction is { } surface)
        {
            userAction = new ExecuteUserActionResponse(
                surface.Task.Type,
                surface.Task.Owner.Value,
                surface.Task.Name,
                surface.Attempt.Value.ToString("N"),
                surface.Module.Type,
                surface.Module.Owner.Value,
                surface.Module.Name,
                surface.ModuleId,
                surface.DisplayText,
                surface.ActionReference.Id.ToString("N"),
                surface.ActionReference.ExpiresAt,
                surface.ActionEpoch.ToString("N"),
                surface.ParkRevision,
                surface.ExpiresAt,
                surface.Completer.Type,
                surface.Completer.Owner.Value,
                surface.Completer.Name);
        }

        return Results.Ok(new ExecuteResponse(outcome.Succeeded, code, userAction));
    }
    catch (BehaviorHostException exception)
    {
        return Results.Content(exception.Reason, "text/plain", statusCode: StatusCodes.Status400BadRequest);
    }
});

app.Run();

// The silo owns the ceiling; a host that claims a wider budget only ever gets the ceiling.
static int ClampHops(int? claimed)
    => Math.Clamp(claimed ?? BehaviorFactEmission.MaximumHops, 0, BehaviorFactEmission.MaximumHops);

static OwnerId RequireOwner(string? value, string reason)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new BehaviorHostException(reason);
    }

    return new OwnerId(value);
}

internal sealed record DeployRequest(
    string Owner,
    string Behavior,
    string ArtifactHash,
    string ArtifactBytesBase64,
    string AssemblyBytesBase64,
    string SignatureBase64);

internal sealed record ActivationRequest(string Owner, string Behavior, string ArtifactHash);

internal sealed record ExecuteRequest(
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
    CapabilityEdgeRequest[] Capabilities,
    DateTimeOffset UtcNow,
    string? WorkerType = null,
    string? WorkerOwner = null,
    string? WorkerName = null,
    int? Hops = null);

internal sealed record CapabilityEdgeRequest(
    string TargetType,
    string TargetOwner,
    string TargetName,
    string RequestId,
    int RequestVersion,
    string ResponseId,
    int ResponseVersion);

internal sealed record ExecuteResponse(
    bool Succeeded,
    string Outcome,
    ExecuteUserActionResponse? UserAction = null);

internal sealed record ExecuteUserActionResponse(
    string TaskType,
    string TaskOwner,
    string TaskName,
    string Attempt,
    string ModuleType,
    string ModuleOwner,
    string ModuleName,
    string ModuleId,
    string DisplayText,
    string ActionReferenceId,
    DateTimeOffset? ActionReferenceExpiresAt,
    string ActionEpoch,
    long ParkRevision,
    DateTimeOffset ExpiresAt,
    string CompleterType,
    string CompleterOwner,
    string CompleterName);
