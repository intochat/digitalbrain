using DigitalBrain.AI.Agents;
using DigitalBrain.Apps;
using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Assistant;

internal static class PackageEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var registry = endpoints.MapGroup("/packages").AddEndpointFilter(PackageRouteGuard.Guard);
        registry.MapGet("", (PackageService service) => service.List());
        registry.MapGet("/{owner}/{name}", (string owner, string name, PackageService service) => service.Read(PackageId.Create(owner, name)));
        registry.MapGet("/{owner}/{name}/revisions/{revision}", (string owner, string name, string revision, PackageService service)
            => service.ReadRevision(PackageId.Create(owner, name), revision));
        registry.MapPost("/{owner}/{name}/revisions", (string owner, string name, CommitPackageRequest request, PackageService service)
            => service.Commit(PackageId.Create(owner, name), request));
        registry.MapPost("/{owner}/{name}/fork", (string owner, string name, ForkPackageRequest request, PackageService service)
            => service.Fork(PackageId.Create(owner, name), request));
        registry.MapPost("/{owner}/{name}/pull", (string owner, string name, PullPackageRequest request, PackageService service)
            => service.Pull(PackageId.Create(owner, name), request));
        registry.MapPost("/{owner}/{name}/proposals", (string owner, string name, ProposePackageRequest request, PackageService service)
            => service.Propose(PackageId.Create(owner, name), request));
        registry.MapPost("/{owner}/{name}/proposals/{number:int}/accept", (string owner, string name, int number, PackageService service)
            => service.Accept(PackageId.Create(owner, name), number));
        registry.MapPost("/{owner}/{name}/proposals/{number:int}/close", (string owner, string name, int number, PackageService service)
            => service.Close(PackageId.Create(owner, name), number));
        registry.MapPost("/{owner}/{name}/publish", (string owner, string name, PublishPackageRequest request, PackageService service)
            => service.Publish(PackageId.Create(owner, name), request));

        var installed = BrainRoutes.Group(endpoints, "/packages/{owner}/{name}").AddEndpointFilter(PackageRouteGuard.Guard);
        installed.MapGet("", (string owner, string name, PackageService service)
            => service.ReadApp(PackageId.Create(owner, name)));
        installed.MapGet("/accounts", (string owner, string name, string? revision, PackageService service)
            => service.AccountOptions(PackageId.Create(owner, name), revision));
        installed.MapPost("", (string owner, string name, InstallPackageRequest request, PackageService service)
            => service.Install(PackageId.Create(owner, name), request));
        installed.MapPost("/configure", (string owner, string name, ConfigurePackageRequest request, PackageService service)
            => service.Configure(PackageId.Create(owner, name), request));
        installed.MapPost("/upgrade", (string owner, string name, UpgradePackageRequest request, PackageService service)
            => service.Upgrade(PackageId.Create(owner, name), request));
        installed.MapDelete("", (string owner, string name, PackageService service)
            => service.Uninstall(PackageId.Create(owner, name)));
        installed.MapPost("/invocations", (string owner, string name, InvokePackageRequest request, PackageService service)
            => service.Invoke(PackageId.Create(owner, name), request));
        installed.MapGet("/invocations/{invocationId:guid}", (string owner, string name, Guid invocationId, PackageService service)
            => service.ReadInvocation(PackageId.Create(owner, name), invocationId));

        BuiltInSettingsEndpoints.Map(endpoints);
    }
}

public static class PackageRouteGuard
{
    // Packages run code, so they share the C# console's developer-mode gate.
    public static async ValueTask<object?> Guard(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (!AgentToolPolicy.DeveloperModeEnabled(configuration["IntoChat:DeveloperMode"])) { return Results.NotFound(); }
        try { return await next(context); }
        catch (ArgumentException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest); }
        catch (UnauthorizedAccessException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status403Forbidden); }
        catch (KeyNotFoundException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status404NotFound); }
        catch (InvalidOperationException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status409Conflict); }
        catch (InvalidDataException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status422UnprocessableEntity); }
    }
}
