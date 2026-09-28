using DigitalBrain.Apps;
using DigitalBrain.Apps.Manifests;
using DigitalBrain.Compute;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Core.Enforcement;
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
using IntoChat.Workspace;
using Microsoft.Extensions.Options;
using DigitalBrain.Identity;
namespace IntoChat.Apps;

internal static class AppEndpoints
{
    public static void MapLocalApps(this IEndpointRouteBuilder routes)
    {
        var apps = routes.MapGroup("/workspaces/{workspaceId}/apps").AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync);
        apps.MapGet("", (string workspaceId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
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
                    examplePrompts = manifest.ExamplePrompts,
                    permissions = manifest.Permissions.Select(permission => new
                    {
                        semanticTypeId = permission.SemanticTypeId,
                        reason = permission.Reason,
                        write = permission.Write,
                    }),
                    meters = manifest.Meters.Select(meter => new
                    {
                        meterId = meter.MeterId,
                        unit = meter.Unit,
                        aggregation = meter.Aggregation,
                        proposedPriceInCompute = meter.ProposedPriceInCompute,
                    }),
                })
                .ToArray();
            return Results.Ok(manifests);
        }));
        apps.MapGet("/{appId}/consent", (string workspaceId, string appId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
        {
            var scope = Scope(auth.Value, workspaceId);
            var sheet = await brain.Get<IAppConsent>(scope).Review(appId).WaitAsync(ct);
            return Results.Ok(sheet);
        }));
        apps.MapPost("/{appId}/consent/approve", (string workspaceId, string appId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
        {
            var scope = Scope(auth.Value, workspaceId);
            var sheet = await brain.Get<IAppConsent>(scope).Approve(appId).WaitAsync(ct);
            return Results.Ok(sheet);
        }));
        apps.MapGet("/node", (string workspaceId, string kind, string name, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
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
        apps.MapPost("/event", (string workspaceId, UiEvent input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Respond(async () =>
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
                        if (!SecretRef.IsReference(input.Value))
                        {
                            throw new ArgumentException("A form secret carries the vault reference, never a raw secret value.");
                        }

                        await form.SetSecret(input.Field ?? "", SecretRef.FromReference(input.Value!, input.Field ?? "")).WaitAsync(ct);
                    }
                    else { await form.SetDraft(input.Field ?? "", input.Value ?? "").WaitAsync(ct); }
                    break;
                default: throw new ArgumentException("Unknown UI event.");
            }
            return Results.Ok(new { delivered = true });
        }));

    }
    private static string Scope(BasicAuthOptions auth, string workspace) => WorkspaceScope.Current(auth, workspace).Id;
    private static async Task<IResult> Respond(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Results.Json(new { error = "This file belongs to another workspace." }, statusCode: 403); }
        catch (FileNotFoundException) { return Results.NotFound(new { error = "The file no longer exists. Refresh Files." }); }
        catch (KeyNotFoundException) { return Results.NotFound(new { error = "The requested image or operation was not found." }); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
        catch (IOException error) { return Results.Json(new { error = error.Message.Contains("changed since", StringComparison.Ordinal) ? error.Message : "The local file could not be accessed. Check its permissions and retry." }, statusCode: 503); }
    }
    internal sealed record UiEvent(string Kind, string Name, string? Action = null, string? Value = null, long Revision = 0, string? Field = null);
}
