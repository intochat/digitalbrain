using Microsoft.AspNetCore.Http;

namespace DigitalBrain.Sdk.Http;

public static class ModuleHttp
{
    public static async Task<IResult> Respond(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Results.Json(new { error = "This item belongs to another brain." }, statusCode: 403); }
        catch (FileNotFoundException) { return Results.NotFound(new { error = "The file no longer exists. Refresh Files." }); }
        catch (KeyNotFoundException) { return Results.NotFound(new { error = "The requested image or operation was not found." }); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
        catch (IOException error) { return Results.Json(new { error = error.Message.Contains("changed since", StringComparison.Ordinal) ? error.Message : "The local file could not be accessed. Check its permissions and retry." }, statusCode: 503); }
    }
}
