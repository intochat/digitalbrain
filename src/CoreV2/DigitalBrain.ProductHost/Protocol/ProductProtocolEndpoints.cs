using System.Globalization;
using System.Text.Json;
using Brain.Abstractions.Activities;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;
using Brain.Modules.UI.Contracts;
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
        endpoints.MapPost("/v2/chat", ChatAsync);
        endpoints.MapPost("/v2/operations/{operationId}:invoke", InvokeAsync);
        endpoints.MapGet("/v2/activities/{activityId:guid}", GetActivityAsync);
        endpoints.MapGet("/v2/activities/{activityId:guid}/events", StreamActivityAsync);
        endpoints.MapGet("/v2/activities/{activityId:guid}/journal", GetJournalAsync);
        endpoints.MapGet("/v2/activities/{activityId:guid}/journal/events", StreamJournalAsync);
        endpoints.MapGet("/v2/brain", GetBrainAsync);
        endpoints.MapGet("/v2/brain/events", StreamBrainAsync);
        return endpoints;
    }

    private static Task<IReadOnlyList<BrainModuleDescriptor>> GetModulesAsync(
        IProductRuntimeClient runtime,
        CancellationToken cancellationToken)
        => runtime.GetModulesAsync(cancellationToken);

    private static Task<IReadOnlyList<BrainOperationDescriptor>> GetOperationsAsync(
        IProductRuntimeClient runtime,
        CancellationToken cancellationToken)
        => runtime.GetOperationsAsync(cancellationToken);

    private static async Task<IResult> ChatAsync(
        ChatSendInput input,
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

        if (string.IsNullOrWhiteSpace(input.Message))
        {
            return Results.Problem(
                "A non-empty message is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var caller = ProductCaller.From(context);
        try
        {
            return Results.Ok(await ProductChat.SendAsync(
                runtime,
                input.Message,
                caller.Workspace,
                caller.Principal,
                idempotencyKey,
                cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

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
        var invocation = new BrainOperationInvocation(
            operationId,
            request.Input.GetRawText(),
            caller.Workspace,
            caller.Principal,
            idempotencyKey);
        try
        {
            var receipt = await runtime.InvokeAsync(invocation, cancellationToken);
            return Results.Accepted($"/v2/activities/{receipt.ActivityId:N}", receipt);
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

            if (activity.Status is ActivityStatus.Completed or ActivityStatus.Failed)
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

    private static async Task<IResult> GetJournalAsync(
        Guid activityId,
        HttpContext context,
        IProductRuntimeClient runtime,
        CancellationToken cancellationToken)
    {
        var caller = ProductCaller.From(context);
        var activity = await runtime.GetActivityAsync(activityId, caller.Workspace, cancellationToken);
        if (activity is null)
        {
            return Results.NotFound();
        }

        var afterSequence = ParseSequence(context.Request.Query["afterSequence"].ToString());
        var take = ParseTake(context.Request.Query["take"].ToString());
        return Results.Ok(await runtime.GetJournalAsync(
            activityId,
            caller.Workspace,
            afterSequence,
            take,
            cancellationToken));
    }

    private static async Task StreamJournalAsync(
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
            var page = await runtime.GetJournalAsync(
                activityId,
                caller.Workspace,
                lastSequence,
                100,
                cancellationToken);
            foreach (var record in page.Records)
            {
                await WriteEventAsync(context, record.Sequence, "journal", record, cancellationToken);
                lastSequence = record.Sequence;
            }

            activity = await runtime.GetActivityAsync(activityId, caller.Workspace, cancellationToken);
            if (activity is null
                || activity.Status is ActivityStatus.Completed or ActivityStatus.Failed
                    && lastSequence >= activity.Sequence)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    private static Task<BrainSnapshot> GetBrainAsync(
        HttpContext context,
        IProductRuntimeClient runtime,
        CancellationToken cancellationToken)
    {
        var caller = ProductCaller.From(context);
        return runtime.GetBrainAsync(caller.Workspace, cancellationToken);
    }

    private static async Task StreamBrainAsync(
        HttpContext context,
        IProductRuntimeClient runtime,
        CancellationToken cancellationToken)
    {
        var caller = ProductCaller.From(context);
        var lastSequence = ParseLastEventId(context.Request.Headers["Last-Event-ID"].ToString());
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        while (!cancellationToken.IsCancellationRequested)
        {
            var snapshot = await runtime.GetBrainAsync(caller.Workspace, cancellationToken);
            if (snapshot.Sequence > lastSequence)
            {
                await WriteEventAsync(context, snapshot.Sequence, "brain", snapshot, cancellationToken);
                lastSequence = snapshot.Sequence;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    private static async Task WriteEventAsync<T>(
        HttpContext context,
        long sequence,
        string eventName,
        T value,
        CancellationToken cancellationToken)
    {
        await context.Response.WriteAsync(
            $"id: {sequence.ToString(CultureInfo.InvariantCulture)}\n",
            cancellationToken);
        await context.Response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await context.Response.WriteAsync(
            $"data: {JsonSerializer.Serialize(value, JsonOptions)}\n\n",
            cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }

    private static long ParseLastEventId(string value)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : 0;

    private static long ParseSequence(string value) => ParseLastEventId(value);

    private static int ParseTake(string value)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed is >= 1 and <= 500
                ? parsed
                : 100;
}
