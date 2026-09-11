using DigitalBrain.AI;
using DigitalBrain.AI.WebSearch;

namespace DigitalBrain.Kernel;

internal static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/agent/capabilities", (IServiceProvider services, IConfiguration configuration) => Results.Ok(new
        {
            webSearch = services.GetService<IWebSearch>() is not null,
            voice = services.GetService<IAudioTranscriptionService>()?.IsReady == true,
            brain = true,
            graphExecution = configuration.GetValue<bool>("DigitalBrain:Graph:Enabled"),
        }));
        endpoints.MapGet("/workspace/artifacts", (WorkspaceArtifactStore store, CancellationToken ct) =>
            RespondAsync(async () => Results.Ok(await store.ListAsync(ct))));
        endpoints.MapPost("/workspace/artifacts", (CreateWorkspaceArtifact input, WorkspaceArtifactStore store, CancellationToken ct) =>
            RespondAsync(async () => Results.Ok(await store.CreateAsync(input, ct))));
        endpoints.MapGet("/workspace/artifacts/{id}", (string id, WorkspaceArtifactStore store, CancellationToken ct) =>
            RespondAsync(async () => Results.Ok(await store.ReadAsync(id, ct))));
        endpoints.MapPut("/workspace/artifacts/{id}", (string id, UpdateWorkspaceArtifact input, WorkspaceArtifactStore store, CancellationToken ct) =>
            RespondAsync(async () => Results.Ok(await store.UpdateAsync(id, input, ct))));
        endpoints.MapPost("/agent/transcribe", TranscribeAsync);
        return endpoints;
    }

    private static async Task<IResult> TranscribeAsync(HttpContext http, IAudioTranscriptionService transcription, CancellationToken cancellationToken)
    {
        const long maxBytes = 25 * 1024 * 1024;
        if (!transcription.IsReady) { return Results.Json(new { error = transcription.ErrorMessage ?? "Voice transcription is not ready." }, statusCode: 503); }
        if (http.Request.ContentLength > maxBytes + 65536) { return Results.StatusCode(413); }
        if (!http.Request.HasFormContentType) { return Results.StatusCode(415); }
        IFormCollection form;
        try { form = await http.Request.ReadFormAsync(cancellationToken); }
        catch (InvalidDataException) { return Results.BadRequest(new { error = "Invalid audio upload." }); }
        var file = form.Files.GetFile("audio");
        if (file is null || file.Length == 0) { return Results.BadRequest(new { error = "Audio is required." }); }
        if (file.Length > maxBytes) { return Results.StatusCode(413); }
        try
        {
            await using var stream = file.OpenReadStream();
            var text = await transcription.TranscribeAsync(stream, file.FileName, cancellationToken);
            return string.IsNullOrWhiteSpace(text)
                ? Results.UnprocessableEntity(new { error = "No speech was recognized. Try again." })
                : Results.Ok(new { text = text.Trim() });
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Voice").LogWarning(error, "Transcription failed");
            return Results.UnprocessableEntity(new { error = "Transcription failed. Your message has not been sent." });
        }
    }

    private static async Task<IResult> RespondAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (ArtifactConflictException error) { return Results.Conflict(new { error = error.Message }); }
        catch (KeyNotFoundException error) { return Results.NotFound(new { error = error.Message }); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
    }
}
