using System.Globalization;
using System.Text.Json;
using Brain.Runtime.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.ProductHost.Protocol;

public static class ProductProtocolEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapProductProtocol(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/v2/modules", GetModulesAsync);
        endpoints.MapGet("/v2/operations", GetOperationsAsync);
        endpoints.MapPost("/v2/operations/{operationId}:invoke", InvokeAsync);
        endpoints.MapGet("/v2/activities/{activityId:guid}", GetActivityAsync);
        endpoints.MapGet("/v2/activities/{activityId:guid}/events", StreamActivityAsync);
        return endpoints;
    }

    private static Task<IReadOnlyList<RuntimeModuleDescriptor>> GetModulesAsync(
        IProductRuntimeClient runtime,
        CancellationToken cancellationToken)
        => runtime.GetModulesAsync(cancellationToken);

    private static Task<IReadOnlyList<RuntimeOperationDescriptor>> GetOperationsAsync(
        IProductRuntimeClient runtime,
        CancellationToken cancellationToken)
        => runtime.GetOperationsAsync(cancellationToken);

    private static async Task<IResult> InvokeAsync(
        string operationId,
        ProductInvocationRequest request,
        HttpContext context,
        IProductRuntimeClient runtime,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        if (idempotencyKey.Length == 0)
        {
            return Results.Problem(
                "The Idempotency-Key header is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var caller = ProductCaller.From(context);
        operationId = Uri.UnescapeDataString(operationId);
        var invocation = new RuntimeInvocation(
            operationId,
            request.Input.GetRawText(),
            caller.Workspace,
            caller.Principal,
            idempotencyKey);
        try
        {
            var receipt = await runtime.InvokeAsync(invocation, cancellationToken);
            return Results.Accepted($"/v2/activities/{receipt.Activity:N}", receipt);
        }
        catch (KeyNotFoundException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> GetActivityAsync(
        Guid activityId,
        HttpContext context,
        IProductRuntimeClient runtime,
        CancellationToken cancellationToken)
    {
        var caller = ProductCaller.From(context);
        var activity = await runtime.GetActivityAsync(
            activityId,
            caller.Workspace,
            cancellationToken);
        return activity is null ? Results.NotFound() : Results.Ok(activity);
    }

    private static async Task StreamActivityAsync(
        Guid activityId,
        HttpContext context,
        IProductRuntimeClient runtime,
        CancellationToken cancellationToken)
    {
        var caller = ProductCaller.From(context);
        var lastSequence = ParseLastEventId(context.Request.Headers["Last-Event-ID"].ToString());
        var activity = await runtime.GetActivityAsync(activityId, caller.Workspace, cancellationToken);
        if (activity is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        while (!cancellationToken.IsCancellationRequested)
        {
            if (activity.Sequence > lastSequence)
            {
                await context.Response.WriteAsync(
                    $"id: {activity.Sequence.ToString(CultureInfo.InvariantCulture)}\n",
                    cancellationToken);
                await context.Response.WriteAsync("event: activity\n", cancellationToken);
                await context.Response.WriteAsync(
                    $"data: {JsonSerializer.Serialize(activity, JsonOptions)}\n\n",
                    cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
                lastSequence = activity.Sequence;
            }

            if (activity.Status is RuntimeActivityStatus.Completed or RuntimeActivityStatus.Failed)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            activity = await runtime.GetActivityAsync(activityId, caller.Workspace, cancellationToken);
            if (activity is null)
            {
                return;
            }
        }
    }

    private static long ParseLastEventId(string value)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : 0;
}
