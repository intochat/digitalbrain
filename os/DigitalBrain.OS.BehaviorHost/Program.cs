using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;
using DigitalBrain.Aspire;
using DigitalBrain.Behaviors;
using DigitalBrain.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddDigitalBrainClient();
builder.Services.AddBehaviorHostEngine(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IBehaviorCapabilityResolver>(static services =>
    new HostGrainCapabilityResolver(
        services.GetRequiredService<IGrainFactory>(),
        new OwnerId(DigitalBrainClientHostingExtensions.ResolveOwner(services.GetRequiredService<IConfiguration>()))));

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
    IBehaviorCapabilityResolver capabilities,
    TimeProvider time,
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
                body.TriggerTypeName,
                body.TriggerJson,
                capabilities,
                time),
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
    string TriggerJson);

internal sealed record ExecuteResponse(bool Succeeded, string Outcome);

file sealed class HostGrainCapabilityResolver(IGrainFactory grains, OwnerId owner) : IBehaviorCapabilityResolver
{
    public TContract Get<TContract>(string name)
        where TContract : class, INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return grains.GetGrain<TContract>(NeuronId.For<TContract>(owner, name).ToGrainId());
    }
}
