using DigitalBrain.Contracts;
using DigitalBrain.Apps;
using DigitalBrain.Apps.Manifests;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Card;
using DigitalBrain.Flutter.Collection;
using DigitalBrain.Flutter.Form;
using DigitalBrain.Flutter.ImageCanvas;
using DigitalBrain.Flutter.Layout;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.Tabs;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Flutter.Workspace;
using IntoChat.LocalFiles;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;
namespace IntoChat.Apps;

internal static class AppEndpoints
{
    public static void MapLocalApps(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/workspaces/{workspaceId}/apps", (string workspaceId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
        {
            var scope = Scope(auth.Value, workspaceId);
            var installed = await brain.Get<IAppCatalog>(scope).List().WaitAsync(ct);
            var manifests = FirstPartyApps.All()
                .Concat(installed.Select(installation => installation.Manifest))
                .GroupBy(manifest => manifest.Id, StringComparer.Ordinal)
                .Select(group => group.Last())
                .OrderBy(manifest => manifest.Name, StringComparer.Ordinal)
                .Select(manifest => new
                {
                    id = manifest.Id,
                    name = manifest.Name,
                    description = manifest.DescriptionForPeople,
                    kind = manifest.Kind.ToString().ToLowerInvariant(),
                    uiEntry = manifest.UiEntry,
                })
                .ToArray();
            return Results.Ok(manifests);
        }));
        routes.MapGet("/workspaces/{workspaceId}/apps/files", (string workspaceId, string? folderId, int? offset, string? sort, string? filter, IDigitalBrain brain, LocalFileStore files, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
        {
            var scope = Scope(auth.Value, workspaceId);
            var neuron = brain.Get<IFileExplorer>(scope);
            var state = await neuron.Navigate(folderId, offset ?? 0, sort ?? "name", filter ?? "").WaitAsync(ct);
            var page = await files.ListAsync(scope, state.FolderId, offset ?? 0, sort ?? "name", filter ?? "", ct);
            await EnsureWindow(brain, scope, "app-files", "Files", state.Surface!, ct);
            return Results.Ok(new { state, page });
        }));
        routes.MapPost("/workspaces/{workspaceId}/apps/files/open", (string workspaceId, OpenImage input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
        {
            var scope = Scope(auth.Value, workspaceId);
            var document = await brain.Get<IFileExplorer>(scope).OpenImage(input.EntryId).WaitAsync(ct);
            await EnsureWindow(brain, scope, "app-images", "Image Editor", new("surface", scope + "/apps/image-editor/surface"), ct);
            return Results.Ok(document);
        }));
        routes.MapGet("/workspaces/{workspaceId}/apps/images-ui", (string workspaceId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
        {
            var name = Scope(auth.Value, workspaceId) + "/apps/image-editor";
            return Results.Ok(new { surface = new UiChildRef("surface", name + "/surface"), tabs = await brain.Get<ITabs>(name + "/tabs").Read().WaitAsync(ct) });
        }));
        routes.MapGet("/workspaces/{workspaceId}/apps/images/{documentId}", (string workspaceId, string documentId, IDigitalBrain brain, AppSurfaceComposer surfaces, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
        {
            var scope = Scope(auth.Value, workspaceId);
            var document = await Document(brain, scope, documentId).Read().WaitAsync(ct);
            if (document.Asset is null) { throw new KeyNotFoundException(); }
            await surfaces.RegisterDocument(scope, document);
            return Results.Ok(document);
        }));
        routes.MapPost("/workspaces/{workspaceId}/apps/images/{documentId}/edit", (string workspaceId, string documentId, EditImage input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
            Results.Ok(await Document(brain, Scope(auth.Value, workspaceId), documentId).Apply(input.Command, input.ExpectedRevision, input.OperationId).WaitAsync(ct))));
        routes.MapPost("/workspaces/{workspaceId}/apps/images/{documentId}/prepare-save", (string workspaceId, string documentId, PrepareImageSave input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
            Results.Ok(await Document(brain, Scope(auth.Value, workspaceId), documentId).PrepareSave(input.ExpectedRevision, input.OperationId).WaitAsync(ct))));
        routes.MapPost("/workspaces/{workspaceId}/apps/images/{documentId}/save/{operationId}", (string workspaceId, string documentId, string operationId, HttpRequest request, IDigitalBrain brain, ImageSaveCoordinator saves, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
        {
            var scope = Scope(auth.Value, workspaceId);
            var document = Document(brain, scope, documentId);
            var state = await document.Read().WaitAsync(ct);
            var ticket = state.Saves.GetValueOrDefault(operationId) ?? throw new KeyNotFoundException("Prepare this save first.");
            var result = await saves.Save(scope, ticket, request.Body, ct);
            var updated = await document.CompleteSave(operationId, result).WaitAsync(ct);
            return Results.Ok(new { document = updated, file = result });
        })).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(LocalFilesOptions.MaxExportBytes));
        routes.MapGet("/workspaces/{workspaceId}/apps/assets/{assetId}", (string workspaceId, string assetId, LocalFileStore files, IOptions<BasicAuthOptions> auth) => Respond(() =>
            Task.FromResult<IResult>(Results.File(files.OpenAsset(Scope(auth.Value, workspaceId), assetId), "application/octet-stream", enableRangeProcessing: true))));
        routes.MapGet("/workspaces/{workspaceId}/apps/node", (string workspaceId, string kind, string name, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
        {
            var scope = Scope(auth.Value, workspaceId);
            if (!name.StartsWith(scope + "/apps/", StringComparison.Ordinal) && !name.StartsWith(scope + "/images/", StringComparison.Ordinal)) { throw new UnauthorizedAccessException("The UI belongs to another workspace."); }
            return kind switch
            {
                "text" => Results.Ok(await brain.Get<IText>(name).Read().WaitAsync(ct)),
                "button" => Results.Ok(await brain.Get<IButton>(name).Read().WaitAsync(ct)),
                "textfield" => Results.Ok(await brain.Get<ITextField>(name).Read().WaitAsync(ct)),
                "card" => Results.Ok(await brain.Get<ICard>(name).Read().WaitAsync(ct)),
                "tabs" => Results.Ok(await brain.Get<ITabs>(name).Read().WaitAsync(ct)),
                "surface" => Results.Ok(await brain.Get<ISurface>(name).Read().WaitAsync(ct)),
                "layout" => Results.Ok(await brain.Get<ILayout>(name).Read().WaitAsync(ct)),
                "collection" => Results.Ok(await brain.Get<ICollectionView>(name).Read().WaitAsync(ct)),
                "imagecanvas" => Results.Ok(await brain.Get<IImageCanvas>(name).Read().WaitAsync(ct)),
                "form" => Results.Ok(await brain.Get<IForm>(name).Read().WaitAsync(ct)),
                _ => throw new ArgumentException("Unknown app UI kind.")
            };
        }));
        routes.MapPost("/workspaces/{workspaceId}/apps/event", (string workspaceId, UiEvent input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
        {
            var scope = Scope(auth.Value, workspaceId);
            if (!input.Name.StartsWith(scope + "/apps/", StringComparison.Ordinal) && !input.Name.StartsWith(scope + "/images/", StringComparison.Ordinal)) { throw new UnauthorizedAccessException(); }
            switch (input.Kind)
            {
                case "button": await brain.Get<IButton>(input.Name).Click().WaitAsync(ct); break;
                case "textfield": await brain.Get<ITextField>(input.Name).SetValue(input.Value ?? "").WaitAsync(ct); break;
                case "tabs": await brain.Get<ITabs>(input.Name).Select(input.Value ?? "").WaitAsync(ct); break;
                case "collection":
                    var collection = brain.Get<ICollectionView>(input.Name);
                    if (input.Action == "select") { await collection.Select(input.Value ?? "", input.Revision).WaitAsync(ct); }
                    else { await collection.Activate(input.Value ?? "", input.Revision).WaitAsync(ct); }
                    break;
                case "form":
                    var form = brain.Get<IForm>(input.Name);
                    if (input.Action == "submit")
                    {
                        var state = await form.Read().WaitAsync(ct);
                        var values = state.Fields.Select(field => new FormFieldValue(field.Name, field.Value)).ToArray();
                        await form.Submit(new(values, (int)input.Revision)).WaitAsync(ct);
                    }
                    else if (input.Action == "secret")
                    {
                        await form.SetSecret(input.Field ?? "", DigitalBrain.Contracts.Types.SecretRef.For(scope, input.Value ?? "", input.Field ?? "", isSet: true)).WaitAsync(ct);
                    }
                    else { await form.SetDraft(input.Field ?? "", input.Value ?? "").WaitAsync(ct); }
                    break;
                default: throw new ArgumentException("Unknown UI event.");
            }
            return Results.Ok(new { delivered = true });
        }));

    }
    private static async Task EnsureWindow(IDigitalBrain brain, string scope, string id, string title, UiChildRef surface, CancellationToken ct)
    {
        var workspace = brain.Get<IWorkspace>(scope);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var state = await workspace.Read().WaitAsync(ct);
            if (state.Windows.Any(w => w.Id == id && w.IsOpen && w.Surface == surface)) { return; }
            try { await workspace.OpenSurface(new(Guid.NewGuid().ToString(), id, title, surface, state.Revision)).WaitAsync(ct); return; }
            catch (WorkspaceRevisionConflictException) when (attempt < 2) { }
        }
    }
    private static string Scope(BasicAuthOptions auth, string workspace) => WorkspaceScope.Create(auth.Username is { Length: > 0 } owner ? owner : AccountSession.DefaultLogin, workspace).Id;
    private static IImageDocument Document(IDigitalBrain brain, string scope, string id)
    {
        if (id.Length != 64 || id.Any(c => !char.IsAsciiHexDigit(c))) { throw new ArgumentException("Invalid image document."); }
        return brain.Get<IImageDocument>(scope + "/images/" + id);
    }
    private static async Task<IResult> Respond(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Results.Json(new { error = "This file is outside the allowed workspace or is a linked file." }, statusCode: 403); }
        catch (FileNotFoundException) { return Results.NotFound(new { error = "The file no longer exists. Refresh Files." }); }
        catch (DirectoryNotFoundException) { return Results.NotFound(new { error = "The folder is unavailable. Check the local Downloads configuration." }); }
        catch (KeyNotFoundException) { return Results.NotFound(new { error = "The requested image or operation was not found." }); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
        catch (IOException error) { return Results.Json(new { error = error.Message.Contains("changed since", StringComparison.Ordinal) ? error.Message : "The local file could not be accessed. Check its permissions and retry." }, statusCode: 503); }
    }
    internal sealed record UiEvent(string Kind, string Name, string? Action = null, string? Value = null, long Revision = 0, string? Field = null);
    internal sealed record OpenImage(string EntryId);
    internal sealed record EditImage(ImageEditCommand Command, long ExpectedRevision, string OperationId);
    internal sealed record PrepareImageSave(long ExpectedRevision, string OperationId);
}