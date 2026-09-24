using System.Text.RegularExpressions;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Core;
using IntoChat.Apps.BuiltIn;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;
using DigitalBrain.Marketplace.Creators;

namespace IntoChat.Apps;

internal static partial class AppRuntimeEndpoints
{
    private const string MarketplaceKey = "marketplace-publishing";

    public static void MapAppRuntime(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/marketplace/apps", async (IDigitalBrain brain) => Results.Ok(
            (await brain.Get<ICreatorPublishing>(MarketplaceKey).List())
            .Where(listing => listing.State == CreatorListingState.Published && listing.Manifest.Composition is not null && listing.AppId.Contains('/'))));

        var apps = routes.MapGroup("/workspaces/{workspaceId}/app-runtime")
            .AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync)
            .AddEndpointFilter(async (context, next) =>
            {
                try { return await next(context); }
                catch (AppManifestException error) { return Results.BadRequest(new { error = error.Message }); }
                catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
                catch (KeyNotFoundException error) { return Results.NotFound(new { error = error.Message }); }
                catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
            });

        apps.MapGet("", async (string workspaceId, WorkspaceApps service) => Results.Ok(await service.List(workspaceId)));
        apps.MapPost("/activate", async (string workspaceId, WorkspaceApps service, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, IEnumerable<AppRegistration> available) =>
        {
            if (available.Any(app => app.Definition.Id == "intochat.assistant"))
            { await brain.Get<IAssistantApp>(WorkspaceScope.Current(auth.Value, workspaceId).Id).Activate(); }
            return Results.Ok(await service.ActivateWorkspace(workspaceId));
        });
        apps.MapPost("", async (string workspaceId, CreateApp input, HttpContext http, WorkspaceApps service) =>
        {
            var caller = CallerContextStamper.Require();
            if (http.User.Identity?.IsAuthenticated != true) { return Results.Unauthorized(); }
            if (!Slug().IsMatch(input.Name ?? "")) { return Results.BadRequest(new { error = "Use a lowercase app name with letters, numbers and hyphens." }); }
            if (input.Manifest is null) { return Results.BadRequest(new { error = "An app manifest is required." }); }
            var manifest = input.Manifest with { Id = caller.PrincipalId + "/" + input.Name, Publisher = caller.PrincipalId };
            ValidatePortable(manifest);
            return Results.Ok(await service.Install(workspaceId, manifest, new Dictionary<string, string>()));
        });
        apps.MapPost("/install", async (string workspaceId, InstallApp input, HttpContext http, IDigitalBrain brain, WorkspaceApps service) =>
        {
            if (http.User.Identity?.IsAuthenticated != true) { return Results.Unauthorized(); }
            var publisher = brain.Get<ICreatorPublishing>(MarketplaceKey);
            var listing = await publisher.ReadVersion(input.Publisher + "/" + input.Name, input.Version);
            if (listing is null) { return Results.NotFound(); }
            if (listing.State != CreatorListingState.Published) { return Results.Conflict(new { error = "This app is unavailable." }); }
            ValidatePortable(listing.Manifest);
            return Results.Ok(await service.Install(workspaceId, listing.Manifest, input.Configuration));
        });
        apps.MapGet("/{publisher}/{name}", async (string workspaceId, string publisher, string name, WorkspaceApps service) =>
            Results.Ok(await service.Get(workspaceId, publisher + "/" + name).Read()));
        apps.MapPost("/{publisher}/{name}/configure", async (string workspaceId, string publisher, string name, ConfigureApp input, WorkspaceApps service) =>
            Results.Ok(await service.Get(workspaceId, publisher + "/" + name).Configure(input.Configuration)));
        apps.MapPost("/{publisher}/{name}/run", async (string workspaceId, string publisher, string name, AppDispatch input, WorkspaceApps service) =>
            Results.Ok(await service.Get(workspaceId, publisher + "/" + name).Dispatch(input)));
        apps.MapPost("/{publisher}/{name}/activate", async (string workspaceId, string publisher, string name, WorkspaceApps service) =>
            Results.Ok(await service.Get(workspaceId, publisher + "/" + name).Activate()));
        apps.MapPost("/{publisher}/{name}/deactivate", async (string workspaceId, string publisher, string name, WorkspaceApps service) =>
            Results.Ok(await service.Get(workspaceId, publisher + "/" + name).Deactivate()));
        apps.MapPost("/{publisher}/{name}/publish", async (string workspaceId, string publisher, string name, HttpContext http, IDigitalBrain brain, WorkspaceApps service) =>
        {
            var caller = CallerContextStamper.Require();
            if (http.User.Identity?.IsAuthenticated != true) { return Results.Unauthorized(); }
            if (publisher != caller.PrincipalId) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            var snapshot = await service.Get(workspaceId, publisher + "/" + name).Read();
            ValidatePortable(snapshot.Manifest);
            var result = await brain.Get<ICreatorPublishing>(MarketplaceKey).Publish(new() { Manifest = snapshot.Manifest, Caller = caller });
            return result.Outcome switch
            {
                CreatorPublishOutcome.Published => Results.Ok(result.Listing),
                CreatorPublishOutcome.IdentityDenied or CreatorPublishOutcome.CallFilterDenied => Results.StatusCode(StatusCodes.Status403Forbidden),
                _ => Results.Conflict(new { error = result.Explanation ?? result.Outcome.ToString() }),
            };
        });
    }

    private static void ValidatePortable(AppManifest manifest)
    {
        ManifestValidator.Validate(manifest);
        if (manifest.Kind != AppKind.Declarative || manifest.Composition is null || manifest.Windows.Count != 0 || manifest.UiEntry is not null || manifest.RemoteEndpoint is not null)
        { throw new AppManifestException("A composed app must contain portable behavior bindings, without author-specific windows, neuron addresses or executable endpoints."); }
        if (manifest.Permissions.Count != 0 || manifest.Meters.Count != 0)
        { throw new AppManifestException("The available local text behaviors do not request external permissions or paid meters."); }
    }

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Slug();
    internal sealed record CreateApp(string Name, AppManifest Manifest);
    internal sealed record InstallApp(string Publisher, string Name, string Version, Dictionary<string, string> Configuration);
    internal sealed record ConfigureApp(Dictionary<string, string> Configuration);
}
