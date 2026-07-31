using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
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
                new OwnerId(body.Owner),
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
        return Results.BadRequest(exception.Reason);
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
                new OwnerId(body.Owner),
                new BehaviorId(body.Behavior),
                body.ArtifactHash),
            cancellationToken);
        return Results.Ok();
    }
    catch (BehaviorHostException exception)
    {
        return Results.BadRequest(exception.Reason);
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
                new OwnerId(body.Owner),
                new BehaviorId(body.Behavior),
                body.ArtifactHash),
            cancellationToken);
        return Results.Ok();
    }
    catch (BehaviorHostException exception)
    {
        return Results.BadRequest(exception.Reason);
    }
});

app.MapPost("/v1/behaviors/execute", async (
    ExecuteRequest body,
    BehaviorHostEngine host,
    CancellationToken cancellationToken) =>
{
    try
    {
        var outcome = await host.ExecuteAsync(
            new BehaviorHostExecuteCommand(
                new BehaviorExecutionMetadata(
                    new OwnerId(body.Owner),
                    new BehaviorId(body.Behavior),
                    new BehaviorRevisionId(body.Revision),
                    new BehaviorExecutionId(Guid.Parse(body.Execution))),
                body.ArtifactHash,
                new NeuronId(body.TaskType, new OwnerId(body.TaskOwner), body.TaskName),
                new AttemptId(Guid.Parse(body.Attempt)),
                body.TriggerTypeName,
                new ProtectedPayloadReference(
                    Guid.Parse(body.TriggerPayloadId),
                    body.TriggerPayloadExpiresAt),
                body.Capabilities
                    .Select(static edge => new BehaviorCapabilityEdge(
                        new NeuronId(edge.TargetType, new OwnerId(edge.TargetOwner), edge.TargetName),
                        edge.RequestId,
                        edge.RequestVersion,
                        edge.ResponseId,
                        edge.ResponseVersion))
                    .ToArray(),
                body.UtcNow),
            cancellationToken);
        return Results.Ok(new ExecuteResponse(outcome.Succeeded, outcome.Outcome));
    }
    catch (BehaviorHostException exception)
    {
        return Results.BadRequest(exception.Reason);
    }
});

app.Run();

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
    DateTimeOffset UtcNow);

internal sealed record CapabilityEdgeRequest(
    string TargetType,
    string TargetOwner,
    string TargetName,
    string RequestId,
    int RequestVersion,
    string ResponseId,
    int ResponseVersion);

internal sealed record ExecuteResponse(bool Succeeded, string Outcome);
