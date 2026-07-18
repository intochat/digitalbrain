using Brain.Contracts;
using Brain.Gateway;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapPost("/ui/action", async (UiActionHttpRequest request, ISurfaceOwner surfaceOwner) =>
{
    var source = new NeuronAddress(
        DevelopmentPrincipal.OrganizationId,
        DevelopmentPrincipal.SpaceId,
        request.ContractId,
        request.InstanceId);
    var gateway = new UiGatewayService(surfaceOwner);
    var receipt = await gateway.ApplyUiActionAsync(request.ActionId, request.ExpectedRevision, source);
    return Results.Ok(receipt);
});

app.MapGet("/ui/surface", async (ISurfaceOwner surfaceOwner) =>
{
    var snapshot = await surfaceOwner.GetSurfaceAsync();
    return Results.Ok(snapshot);
});

app.MapPost("/ui/reconnect", async (ReconnectHttpRequest request, ILiveFeedSubscription live, IDurableFeed durable, ISurfaceOwner surfaceOwner) =>
{
    var session = new UiFeedSession(live, durable, surfaceOwner, request.Cursor);
    var result = await session.ReconnectAsync(request.PageSize <= 0 ? 100 : request.PageSize);
    return Results.Ok(result);
});

app.Run();

public sealed record UiActionHttpRequest(
    string ContractId,
    string InstanceId,
    string ActionId,
    long ExpectedRevision);

public sealed record ReconnectHttpRequest(long Cursor, int PageSize = 100);
