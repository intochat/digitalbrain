using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Apps;
using DigitalBrain.Flutter;
using DigitalBrain.Core.Enforcement;
using IntoChat.Marketplace;
using IntoChat.Packages;

namespace IntoChat.Apps;

internal static class BuiltInAppEndpoints
{
    public static void MapBuiltInApps(this IEndpointRouteBuilder routes)
    {
        // moves with PackageService: the assistant built-in routes already live in the Assistant module.
        var apps = BrainRoutes.Group(routes, "/built-in");
        apps.MapPost("/settings/open", async (IDigitalBrain brain, PackageService packages) =>
            await OpenSettings(BrainScope.CurrentId(), brain, packages));
    }

    // Settings is the shipped settings package: installed on first open, then asked for its surface
    // and preferences, answered in the shape the shell renders.
    private static async Task<IResult> OpenSettings(string workspaceId, IDigitalBrain brain, PackageService packages)
    {
        var package = PackageId.Create(ShippedApps.Publisher, "settings");
        var app = brain.Get<IApp>(packages.AppKey(workspaceId, package));
        var snapshot = await app.Read();
        if (snapshot.Status != AppStatus.Installed)
        {
            // A concurrent open may have installed it first; that outcome is the one we wanted.
            try { await packages.Install(workspaceId, package, new InstallPackageRequest()); }
            catch (InvalidOperationException)
            {
                if ((await app.Read()).Status != AppStatus.Installed) { throw; }
            }
        }
        else if (snapshot.Revision is { } installed
            && (await packages.Read(package)).Published is { } published && installed.Revision != published)
        {
            await packages.Upgrade(workspaceId, package, new UpgradePackageRequest());
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
