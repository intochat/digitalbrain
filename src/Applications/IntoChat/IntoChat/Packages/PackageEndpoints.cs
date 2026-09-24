using DigitalBrain.AI.Agents;
using DigitalBrain.Apps;
using DigitalBrain.Core.Enforcement;

namespace IntoChat.Packages;

internal static class PackageEndpoints
{
    public static void AddPackages(this IHostApplicationBuilder builder) => builder.Services.AddSingleton<PackageService>();

    public static void MapPackages(this IEndpointRouteBuilder routes)
    {
        var packages = routes.MapGroup("/packages").AddEndpointFilter(Guard);
        packages.MapGet("", (PackageService service) => service.List());
        packages.MapGet("/{owner}/{name}", (string owner, string name, PackageService service) => service.Read(PackageId.Create(owner, name)));
        packages.MapGet("/{owner}/{name}/revisions/{revision}", (string owner, string name, string revision, PackageService service)
            => service.ReadRevision(PackageId.Create(owner, name), revision));
        packages.MapPost("/{owner}/{name}/revisions", (string owner, string name, CommitPackageRequest request, PackageService service, CancellationToken ct)
            => service.Commit(PackageId.Create(owner, name), request, ct));
        packages.MapPost("/{owner}/{name}/fork", (string owner, string name, ForkPackageRequest request, PackageService service)
            => service.Fork(PackageId.Create(owner, name), request));
        packages.MapPost("/{owner}/{name}/pull", (string owner, string name, PullPackageRequest request, PackageService service)
            => service.Pull(PackageId.Create(owner, name), request));
        packages.MapPost("/{owner}/{name}/proposals", (string owner, string name, ProposePackageRequest request, PackageService service)
            => service.Propose(PackageId.Create(owner, name), request));
        packages.MapPost("/{owner}/{name}/proposals/{number:int}/accept", (string owner, string name, int number, PackageService service)
            => service.Accept(PackageId.Create(owner, name), number));
        packages.MapPost("/{owner}/{name}/publish", (string owner, string name, PublishPackageRequest request, PackageService service)
            => service.Publish(PackageId.Create(owner, name), request));

        var installed = routes.MapGroup("/workspaces/{workspaceId}/packages/{owner}/{name}")
            .AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync)
            .AddEndpointFilter(Guard);
        installed.MapGet("", (string workspaceId, string owner, string name, PackageService service)
            => service.ReadApp(workspaceId, PackageId.Create(owner, name)));
        installed.MapPost("", (string workspaceId, string owner, string name, InstallPackageRequest request, PackageService service)
            => service.Install(workspaceId, PackageId.Create(owner, name), request));
        installed.MapPost("/configure", (string workspaceId, string owner, string name, ConfigurePackageRequest request, PackageService service)
            => service.Configure(workspaceId, PackageId.Create(owner, name), request));
        installed.MapPost("/upgrade", (string workspaceId, string owner, string name, UpgradePackageRequest request, PackageService service)
            => service.Upgrade(workspaceId, PackageId.Create(owner, name), request));
        installed.MapDelete("", (string workspaceId, string owner, string name, PackageService service)
            => service.Uninstall(workspaceId, PackageId.Create(owner, name)));
        installed.MapPost("/invocations", (string workspaceId, string owner, string name, InvokePackageRequest request, PackageService service)
            => service.Invoke(workspaceId, PackageId.Create(owner, name), request));
        installed.MapGet("/invocations/{invocationId:guid}", (string workspaceId, string owner, string name, Guid invocationId, PackageService service)
            => service.ReadInvocation(workspaceId, PackageId.Create(owner, name), invocationId));
    }

    // Packages run code, so they share the behavior console's developer-mode gate.
    private static async ValueTask<object?> Guard(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (!AgentToolPolicy.DeveloperModeEnabled(configuration["IntoChat:DeveloperMode"])) { return Results.NotFound(); }
        try { return await next(context); }
        catch (PackageCheckFailedException error)
        {
            return Results.Problem(error.Message, statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?> { ["diagnostics"] = error.Check.Diagnostics, ["tests"] = error.Check.Tests });
        }
        catch (ArgumentException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest); }
        catch (UnauthorizedAccessException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status403Forbidden); }
        catch (KeyNotFoundException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status404NotFound); }
        catch (InvalidOperationException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status409Conflict); }
        catch (InvalidDataException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status422UnprocessableEntity); }
    }
}
