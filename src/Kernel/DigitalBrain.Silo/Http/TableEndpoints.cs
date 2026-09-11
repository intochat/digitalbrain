using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal static class TableEndpoints
{
    public static IEndpointRouteBuilder MapTableEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/ui/tables", (TableService tables, CancellationToken cancellationToken) =>
            RespondAsync(async () => Results.Ok(await tables.ListAsync(cancellationToken))));
        endpoints.MapPost("/ui/tables", (CreateTable table, TableService tables, CancellationToken cancellationToken) =>
            RespondAsync(async () => Results.Ok(await tables.CreateAsync(table, cancellationToken))));
        endpoints.MapGet("/ui/tables/{id}", (string id, int? offset, int? limit, TableService tables, CancellationToken cancellationToken) =>
            RespondAsync(async () => Results.Ok(await tables.ReadAsync(id, offset ?? 0, limit ?? 50, cancellationToken))))
            .AddEndpointFilter(new NeuronNameFilter("id"));
        endpoints.MapPut("/ui/tables/{id}/view", (string id, UpdateTableView view, TableService tables, CancellationToken cancellationToken) =>
            RespondAsync(async () => Results.Ok(await tables.UpdateAsync(id, view, cancellationToken))))
            .AddEndpointFilter(new NeuronNameFilter("id"));
        return endpoints;
    }

    private static async Task<IResult> RespondAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (TableRevisionConflictException error)
        {
            return Results.Conflict(new { error = error.Message });
        }
        catch (KeyNotFoundException error)
        {
            return Results.NotFound(new { error = error.Message });
        }
        catch (ArgumentException error)
        {
            return Results.BadRequest(new { error = error.Message });
        }
        catch (TimeoutException)
        {
            return Results.Problem("The table operation has not completed. Reload the saved table before retrying.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
