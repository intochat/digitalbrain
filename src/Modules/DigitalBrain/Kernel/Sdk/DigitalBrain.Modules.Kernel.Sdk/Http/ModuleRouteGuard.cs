using Microsoft.AspNetCore.Http;
namespace DigitalBrain.Sdk.Http;

public static class ModuleRouteGuard
{
    public static async ValueTask<object?> Guard(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (Capacity.CapacityUnavailableException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
        catch (ArgumentException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest); }
        catch (UnauthorizedAccessException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status403Forbidden); }
        catch (KeyNotFoundException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status404NotFound); }
        catch (InvalidOperationException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status409Conflict); }
        catch (InvalidDataException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status422UnprocessableEntity); }
    }
}

