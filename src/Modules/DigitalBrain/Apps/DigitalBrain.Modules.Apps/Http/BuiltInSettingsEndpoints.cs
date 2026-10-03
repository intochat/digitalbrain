using System.Text.Json;
using DigitalBrain;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter;
using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.Kernel.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Apps;

internal static class BuiltInSettingsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var builtIn = BrainRoutes.Group(endpoints, "/built-in");
        builtIn.MapPost("/settings/open", async (IDigitalBrain brain, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService packages, IOptions<BuiltInSettingsOptions> options)
            => await OpenSettings(brain, packages, options.Value));
    }

    // Settings is the shipped settings package: installed on first open, then asked for its surface
    // and preferences, answered in the shape the shell renders.
    internal static async Task<IResult> OpenSettings(IDigitalBrain brain, AppService packages, BuiltInSettingsOptions options)
    {
        if (options.Package is not { } package)
        {
            return Results.Json(new { error = "This host has no built-in settings package configured." }, statusCode: 503);
        }
        var app = brain.Get<IApp>(BrainScope.CurrentId() + "/packages/" + package);
        var snapshot = await app.Read();
        if (snapshot.Status != AppStatus.Installed)
        {
            // A concurrent open may have installed it first; that outcome is the one we wanted.
            try { await packages.Install(package, new InstallAppRequest()); }
            catch (InvalidOperationException)
            {
                if ((await app.Read()).Status != AppStatus.Installed) { throw; }
            }
        }
        else if (snapshot.Revision is { } installed
            && (await packages.Read(package)).Published is { } published && installed.Revision != published)
        {
            await packages.Upgrade(package, new UpgradeAppRequest());
        }
        var invocation = await packages.Invoke(package, new InvokeAppRequest("open", ""));
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (invocation.Status == InvocationStatus.Pending && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(200);
            invocation = await packages.ReadInvocation(package, invocation.Id);
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

