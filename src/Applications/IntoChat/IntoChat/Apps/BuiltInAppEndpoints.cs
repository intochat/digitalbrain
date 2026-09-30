using System.Text.Json;
using DigitalBrain.Identity.Configuration;
using DigitalBrain.Contracts;
using DigitalBrain.Apps;
using DigitalBrain.Assistant;
using DigitalBrain.Flutter;
using DigitalBrain.Core.Enforcement;
using IntoChat.Marketplace;
using IntoChat.Packages;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;
using DigitalBrain.Identity;

namespace IntoChat.Apps;

internal static class BuiltInAppEndpoints
{
    public static void MapBuiltInApps(this IEndpointRouteBuilder routes)
    {
        var apps = routes.MapGroup("/workspaces/{workspaceId}/built-in")
            .AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync);
        apps.MapPost("/activate", async (string workspaceId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth) =>
        {
            await brain.Get<IAssistant>(AssistantSurface.Key(WorkspaceScope.Current(auth.Value, workspaceId).Id)).Activate();
            return Results.NoContent();
        });
        apps.MapPost("/{appId}/open", async (string workspaceId, string appId, IDigitalBrain brain, PackageService packages, IOptions<BasicAuthOptions> auth) =>
        {
            var key = WorkspaceScope.Current(auth.Value, workspaceId).Id;
            if (appId == "assistant")
            {
                var window = await brain.Get<IAssistant>(AssistantSurface.Key(key)).OpenWindow();
                return Results.Ok(new { id = window.Id, title = window.Title, kind = "surface", surface = window.Surface });
            }
            if (appId == "settings") { return await OpenSettings(workspaceId, brain, packages); }
            return Results.NotFound();
        });
    }

    // Settings is the shipped settings package: installed on first open, then asked for its surface
    // and preferences, answered in the shape the shell renders.
    private static async Task<IResult> OpenSettings(string workspaceId, IDigitalBrain brain, PackageService packages)
    {
        var package = PackageId.Create(ShippedApps.Publisher, "settings");
        var app = brain.Get<IApp>(packages.AppKey(workspaceId, package));
        if ((await app.Read()).Status != AppStatus.Installed)
        {
            await packages.Install(workspaceId, package, new InstallPackageRequest());
        }
        var invocation = await packages.Invoke(workspaceId, package, new InvokePackageRequest("open", ""));
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (invocation.Status == InvocationStatus.Pending && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(200);
            invocation = await packages.ReadInvocation(workspaceId, package, invocation.Id);
        }
        if (invocation.Status != InvocationStatus.Completed || invocation.Output is null)
        {
            return Results.Json(new { error = invocation.Error ?? "Settings did not answer. Is the C# sandbox running?" }, statusCode: 503);
        }
        using var payload = JsonDocument.Parse(invocation.Output);
        var surfaceName = payload.RootElement.GetProperty("surface").GetString()!;
        return Results.Ok(new
        {
            surface = new { kind = UIVocabulary.SurfaceType, name = surfaceName },
            preferences = payload.RootElement.GetProperty("preferences").Clone(),
        });
    }
}
