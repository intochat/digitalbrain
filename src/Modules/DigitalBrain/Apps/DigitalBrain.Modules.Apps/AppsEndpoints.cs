using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Sdk.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Apps;

internal static class AppsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var apps = BrainRoutes.Group(endpoints, "/apps").AddEndpointFilter(ModuleRouteGuard.Guard);
        apps.MapGet("", (IDigitalBrain brain, CancellationToken ct) => InstalledApps.List(brain, BrainScope.CurrentId(), ct));
        apps.MapPost("/{appId}/open", (string appId, HttpRequest request, IDigitalBrain brain, CancellationToken ct)
            => ModuleHttp.Respond(() => Invoke(appId, "open", request, brain, ct, requireJson: true)));
        apps.MapPost("/{appId}/invoke/{operation}", (string appId, string operation, HttpRequest request, IDigitalBrain brain, CancellationToken ct)
            => ModuleHttp.Respond(() => Invoke(appId, operation, request, brain, ct, requireJson: false)));
    }

    private static async Task<IResult> Invoke(string appId, string operation, HttpRequest request, IDigitalBrain brain, CancellationToken ct, bool requireJson)
    {
        // ASP.NET keeps an encoded slash inside a single route segment.
        appId = Uri.UnescapeDataString(appId);
        PackageId id;
        if (appId.Contains('/', StringComparison.Ordinal)) { id = PackageId.Parse(appId); }
        else
        {
            var installed = await InstalledApps.List(brain, BrainScope.CurrentId(), ct);
            var matches = installed.Where(app => app.Package.Name == appId).ToArray();
            if (matches.Length == 0) { return Results.NotFound(new { error = $"App '{appId}' is not installed in this brain." }); }
            if (matches.Length > 1) { return Results.Conflict(new { error = $"App '{appId}' is ambiguous; use its owner/name id." }); }
            id = matches[0].Package;
        }
        var app = brain.Get<IApp>(InstalledPackages.AppKey(id));
        var snapshot = await app.Read().WaitAsync(ct);
        if (snapshot.Status != AppStatus.Installed || snapshot.UninstallPending)
        { return Results.NotFound(new { error = $"App '{appId}' is not installed in this brain." }); }
        if (!snapshot.Operations.Any(item => item.Name == operation))
        { return Results.UnprocessableEntity(new { error = $"App '{appId}' has no '{operation}' operation." }); }
        using var reader = new StreamReader(request.Body);
        var input = await reader.ReadToEndAsync(ct);
        var invocation = await app.Invoke(new(Guid.NewGuid(), operation, input.Length == 0 ? "{}" : input)).WaitAsync(ct);
        var started = TimeProvider.System.GetTimestamp();
        while (invocation.Status == InvocationStatus.Pending && TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(100, ct);
            invocation = await app.ReadInvocation(invocation.Id).WaitAsync(ct);
        }
        if (invocation.Status == InvocationStatus.Pending)
        { return Results.Json(new { error = "The app has not answered yet.", invocationId = invocation.Id }, statusCode: 408); }
        if (invocation.Status != InvocationStatus.Completed || invocation.Output is null)
        { return Results.UnprocessableEntity(new { error = invocation.Error ?? "The app returned no output.", invocationId = invocation.Id }); }
        try
        {
            using var output = JsonDocument.Parse(invocation.Output);
            return Results.Ok(output.RootElement.Clone());
        }
        catch (JsonException)
        {
            return requireJson
                ? Results.UnprocessableEntity(new { error = "The app returned invalid JSON.", invocationId = invocation.Id })
                : Results.Text(invocation.Output);
        }
    }
}
