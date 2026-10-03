using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Tabs;
using DigitalBrain.Flutter.Workspace;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Files;

internal static class FilesEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var apps = BrainRoutes.Group(endpoints, "/apps");
        apps.MapGet("/files", (int? offset, string? sort, string? filter, IDigitalBrain brain, WorkspaceFileStore files, CancellationToken ct) => Respond(async () =>
        {
            var scope = BrainScope.CurrentId();
            var state = await brain.Get<IFileExplorer>(scope).Navigate(offset ?? 0, sort ?? "name", filter ?? "").WaitAsync(ct);
            var page = await files.ListAsync(scope, offset ?? 0, sort ?? "name", filter ?? "", ct);
            await brain.Get<IWorkspace>(scope).EnsureOpenAsync("app-files", "Files", WindowReference.For(state.Surface!), ct);
            return Results.Ok(new { state, page });
        }));
        apps.MapPost("/files/upload", (string name, HttpRequest request, WorkspaceFileStore files, CancellationToken ct) => Respond(async () =>
            Results.Ok(await files.UploadImageAsync(BrainScope.CurrentId(), name, request.Body, ct))))
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(WorkspaceFileStore.MaxSourceBytes));
        apps.MapPost("/files/open", (OpenImage input, IDigitalBrain brain, CancellationToken ct) => Respond(async () =>
        {
            var scope = BrainScope.CurrentId();
            var document = await brain.Get<IFileExplorer>(scope).OpenImage(input.EntryId).WaitAsync(ct);
            await brain.Get<IWorkspace>(scope).EnsureOpenAsync("app-images", "Image Editor",
                WindowReference.For(new UiChildRef("surface", scope + "/apps/image-editor/surface")), ct);
            return Results.Ok(document);
        }));
        apps.MapGet("/images-ui", (IDigitalBrain brain, CancellationToken ct) => Respond(async () =>
        {
            var name = BrainScope.CurrentId() + "/apps/image-editor";
            return Results.Ok(new { surface = new UiChildRef("surface", name + "/surface"), tabs = await brain.Get<ITabs>(name + "/tabs").Read().WaitAsync(ct) });
        }));
        apps.MapGet("/images/{documentId}", (string documentId, IDigitalBrain brain, FileSurfaces surfaces, WorkspaceFileStore files, CancellationToken ct) => Respond(async () =>
        {
            var scope = BrainScope.CurrentId();
            var document = await Document(brain, scope, documentId).Read().WaitAsync(ct);
            if (document.Asset is null) { throw new KeyNotFoundException(); }
            await files.PreserveAssetAsync(scope, document.Asset, ct);
            await surfaces.RegisterDocument(scope, document);
            return Results.Ok(document);
        }));
        apps.MapPost("/images/{documentId}/edit", (string documentId, EditImage input, IDigitalBrain brain, CancellationToken ct) => Respond(async () =>
            Results.Ok(await Document(brain, BrainScope.CurrentId(), documentId).Apply(input.Command, input.ExpectedRevision, input.OperationId).WaitAsync(ct))));
        apps.MapPost("/images/{documentId}/prepare-save", (string documentId, PrepareImageSave input, IDigitalBrain brain, CancellationToken ct) => Respond(async () =>
            Results.Ok(await Document(brain, BrainScope.CurrentId(), documentId).PrepareSave(input.ExpectedRevision, input.OperationId).WaitAsync(ct))));
        apps.MapPost("/images/{documentId}/save/{operationId}", (string documentId, string operationId, HttpRequest request, IDigitalBrain brain, ImageSaveCoordinator saves, CancellationToken ct) => Respond(async () =>
        {
            var scope = BrainScope.CurrentId();
            var document = Document(brain, scope, documentId);
            var state = await document.Read().WaitAsync(ct);
            var ticket = state.Saves.GetValueOrDefault(operationId) ?? throw new KeyNotFoundException("Prepare this save first.");
            var result = await saves.Save(scope, ticket, request.Body, ct);
            var updated = await document.CompleteSave(operationId, result).WaitAsync(ct);
            return Results.Ok(new { document = updated, file = result });
        })).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(WorkspaceFileStore.MaxExportBytes));
        apps.MapGet("/assets/{assetId}", (string assetId, WorkspaceFileStore files, CancellationToken ct) => Respond(async () =>
            Results.File(await files.OpenAssetAsync(BrainScope.CurrentId(), assetId, ct), "application/octet-stream", enableRangeProcessing: true)));
    }

    private static IImageDocument Document(IDigitalBrain brain, string scope, string id)
    {
        if (id.Length != 64 || id.Any(c => !char.IsAsciiHexDigit(c))) { throw new ArgumentException("Invalid image document."); }
        return brain.Get<IImageDocument>(scope + "/images/" + id);
    }

    private static async Task<IResult> Respond(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Results.Json(new { error = "This file belongs to another brain." }, statusCode: 403); }
        catch (FileNotFoundException) { return Results.NotFound(new { error = "The file no longer exists. Refresh Files." }); }
        catch (KeyNotFoundException) { return Results.NotFound(new { error = "The requested image or operation was not found." }); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
        catch (IOException) { return Results.Json(new { error = "The file could not be accessed. Check its permissions and retry." }, statusCode: 503); }
    }

    internal sealed record OpenImage(string EntryId);
    internal sealed record EditImage(ImageEditCommand Command, long ExpectedRevision, string OperationId);
    internal sealed record PrepareImageSave(long ExpectedRevision, string OperationId);
}
