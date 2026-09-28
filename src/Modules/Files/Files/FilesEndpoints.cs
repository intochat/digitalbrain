using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Tabs;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Files;

internal static class FilesEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var apps = endpoints.MapGroup("/workspaces/{workspaceId}/apps").AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync);
        apps.MapGet("/files", (string workspaceId, int? offset, string? sort, string? filter, IDigitalBrain brain, WorkspaceFileStore files, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
        {
            var scope = Scope(auth.Value, workspaceId);
            var state = await brain.Get<IFileExplorer>(scope).Navigate(offset ?? 0, sort ?? "name", filter ?? "").WaitAsync(ct);
            var page = await files.ListAsync(scope, offset ?? 0, sort ?? "name", filter ?? "", ct);
            await brain.Get<IWorkspace>(scope).EnsureOpenAsync("app-files", "Files", WindowReference.For(state.Surface!), ct);
            return Results.Ok(new { state, page });
        }));
        apps.MapPost("/files/upload", (string workspaceId, string name, HttpRequest request, WorkspaceFileStore files, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
            Results.Ok(await files.UploadImageAsync(Scope(auth.Value, workspaceId), name, request.Body, ct))))
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(WorkspaceFileStore.MaxSourceBytes));
        apps.MapPost("/files/open", (string workspaceId, OpenImage input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
        {
            var scope = Scope(auth.Value, workspaceId);
            var document = await brain.Get<IFileExplorer>(scope).OpenImage(input.EntryId).WaitAsync(ct);
            await brain.Get<IWorkspace>(scope).EnsureOpenAsync("app-images", "Image Editor",
                WindowReference.For(new UiChildRef("surface", scope + "/apps/image-editor/surface")), ct);
            return Results.Ok(document);
        }));
        apps.MapGet("/images-ui", (string workspaceId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
        {
            var name = Scope(auth.Value, workspaceId) + "/apps/image-editor";
            return Results.Ok(new { surface = new UiChildRef("surface", name + "/surface"), tabs = await brain.Get<ITabs>(name + "/tabs").Read().WaitAsync(ct) });
        }));
        apps.MapGet("/images/{documentId}", (string workspaceId, string documentId, IDigitalBrain brain, FileSurfaces surfaces, WorkspaceFileStore files, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
        {
            var scope = Scope(auth.Value, workspaceId);
            var document = await Document(brain, scope, documentId).Read().WaitAsync(ct);
            if (document.Asset is null) { throw new KeyNotFoundException(); }
            await files.PreserveAssetAsync(scope, document.Asset, ct);
            await surfaces.RegisterDocument(scope, document);
            return Results.Ok(document);
        }));
        apps.MapPost("/images/{documentId}/edit", (string workspaceId, string documentId, EditImage input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
            Results.Ok(await Document(brain, Scope(auth.Value, workspaceId), documentId).Apply(input.Command, input.ExpectedRevision, input.OperationId).WaitAsync(ct))));
        apps.MapPost("/images/{documentId}/prepare-save", (string workspaceId, string documentId, PrepareImageSave input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
            Results.Ok(await Document(brain, Scope(auth.Value, workspaceId), documentId).PrepareSave(input.ExpectedRevision, input.OperationId).WaitAsync(ct))));
        apps.MapPost("/images/{documentId}/save/{operationId}", (string workspaceId, string documentId, string operationId, HttpRequest request, IDigitalBrain brain, ImageSaveCoordinator saves, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
        {
            var scope = Scope(auth.Value, workspaceId);
            var document = Document(brain, scope, documentId);
            var state = await document.Read().WaitAsync(ct);
            var ticket = state.Saves.GetValueOrDefault(operationId) ?? throw new KeyNotFoundException("Prepare this save first.");
            var result = await saves.Save(scope, ticket, request.Body, ct);
            var updated = await document.CompleteSave(operationId, result).WaitAsync(ct);
            return Results.Ok(new { document = updated, file = result });
        })).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(WorkspaceFileStore.MaxExportBytes));
        apps.MapGet("/assets/{assetId}", (string workspaceId, string assetId, WorkspaceFileStore files, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
            Results.File(await files.OpenAssetAsync(Scope(auth.Value, workspaceId), assetId, ct), "application/octet-stream", enableRangeProcessing: true)));
    }

    private static string Scope(BasicAuthOptions auth, string workspace) => WorkspaceScope.Current(auth, workspace).Id;

    private static IImageDocument Document(IDigitalBrain brain, string scope, string id)
    {
        if (id.Length != 64 || id.Any(c => !char.IsAsciiHexDigit(c))) { throw new ArgumentException("Invalid image document."); }
        return brain.Get<IImageDocument>(scope + "/images/" + id);
    }

    private static async Task<IResult> Respond(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Results.Json(new { error = "This file belongs to another workspace." }, statusCode: 403); }
        catch (FileNotFoundException) { return Results.NotFound(new { error = "The file no longer exists. Refresh Files." }); }
        catch (KeyNotFoundException) { return Results.NotFound(new { error = "The requested image or operation was not found." }); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
        catch (IOException error) { return Results.Json(new { error = error.Message.Contains("changed since", StringComparison.Ordinal) ? error.Message : "The file could not be accessed. Check its permissions and retry." }, statusCode: 503); }
    }

    internal sealed record OpenImage(string EntryId);
    internal sealed record EditImage(ImageEditCommand Command, long ExpectedRevision, string OperationId);
    internal sealed record PrepareImageSave(long ExpectedRevision, string OperationId);
}
