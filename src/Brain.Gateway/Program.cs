using Brain.Contracts;
using Brain.Gateway;

var builder = WebApplication.CreateBuilder(args);
builder.AddGatewayServices();
var app = builder.Build();

app.MapPost("/ui/action", async (UiActionHttpRequest request, UiGatewayService gateway) =>
{
    var receipt = await gateway.ApplyUiActionAsync(
        request.ContractId,
        request.InstanceId,
        request.ActionId,
        request.ExpectedRevision);
    return Results.Ok(receipt);
});

app.MapGet("/ui/surface", async (string contractId, string instanceId, UiGatewayService gateway) =>
{
    var snapshot = await gateway.GetSnapshotAsync(contractId, instanceId);
    return Results.Ok(snapshot);
});

app.MapPost("/ui/reconnect", async (
    ReconnectHttpRequest request,
    ILiveFeedSubscriptionFactory liveFactory,
    IDurableFeed durable,
    ISurfaceOwnerResolver resolver) =>
{
    var owner = resolver.Resolve(request.ContractId, request.InstanceId);
    await using var session = new UiFeedSession(liveFactory.Create(), durable, owner, request.Cursor);
    var result = await session.ReconnectAsync(request.PageSize <= 0 ? 100 : request.PageSize);
    return Results.Ok(result);
});

app.Run();

public sealed record UiActionHttpRequest(
    string ContractId,
    string InstanceId,
    string ActionId,
    long ExpectedRevision);

public sealed record ReconnectHttpRequest(
    string ContractId,
    string InstanceId,
    long Cursor,
    int PageSize = 100);
