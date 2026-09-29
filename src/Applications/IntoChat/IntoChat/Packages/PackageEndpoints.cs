using DigitalBrain.AI.Agents;
using DigitalBrain.Contracts;
using DigitalBrain.Apps;
using DigitalBrain.Core.Enforcement;
using IntoChat.Marketplace;

namespace IntoChat.Packages;

internal sealed record DraftRequest(string Text);

internal static class PackageEndpoints
{
    public static void AddPackages(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<PackageService>();
        builder.Services.AddSingleton<CSharpSharing>();
        builder.Services.AddSingleton<MarketplaceService>();
        builder.Services.AddAppRuntime<GroupChatRuntime>();
        builder.Services.AddAppRuntime<PromptRuntime>();
        builder.Services.AddHostedService<ShippedAppPublisher>();
    }

    public static void MapPackages(this IEndpointRouteBuilder routes)
    {
        var packages = routes.MapGroup("/packages").AddEndpointFilter(Guard);
        packages.MapGet("", (PackageService service) => service.List());
        packages.MapGet("/{owner}/{name}", (string owner, string name, PackageService service) => service.Read(PackageId.Create(owner, name)));
        packages.MapGet("/{owner}/{name}/revisions/{revision}", (string owner, string name, string revision, PackageService service)
            => service.ReadRevision(PackageId.Create(owner, name), revision));
        packages.MapPost("/{owner}/{name}/revisions", (string owner, string name, CommitPackageRequest request, PackageService service)
            => service.Commit(PackageId.Create(owner, name), request));
        packages.MapPost("/{owner}/{name}/fork", (string owner, string name, ForkPackageRequest request, PackageService service)
            => service.Fork(PackageId.Create(owner, name), request));
        packages.MapPost("/{owner}/{name}/pull", (string owner, string name, PullPackageRequest request, PackageService service)
            => service.Pull(PackageId.Create(owner, name), request));
        packages.MapPost("/{owner}/{name}/proposals", (string owner, string name, ProposePackageRequest request, PackageService service)
            => service.Propose(PackageId.Create(owner, name), request));
        packages.MapPost("/{owner}/{name}/proposals/{number:int}/accept", (string owner, string name, int number, PackageService service)
            => service.Accept(PackageId.Create(owner, name), number));
        packages.MapPost("/{owner}/{name}/proposals/{number:int}/close", (string owner, string name, int number, PackageService service)
            => service.Close(PackageId.Create(owner, name), number));
        packages.MapPost("/drafts/{id}", (string id, DraftRequest request, IDigitalBrain brain)
            => Draft(brain, id).Draft(request.Text));
        packages.MapGet("/drafts/{id}", (string id, IDigitalBrain brain) => Draft(brain, id).Read());
        packages.MapPost("/drafts/{id}/revise", (string id, DraftRequest request, IDigitalBrain brain)
            => Draft(brain, id).Revise(request.Text));
        packages.MapPut("/drafts/{id}/spec", (string id, DraftRequest request, IDigitalBrain brain)
            => Draft(brain, id).EditSpec(request.Text));
        packages.MapPost("/drafts/{id}/build", (string id, IDigitalBrain brain) => Draft(brain, id).Build());
        packages.MapGet("/{owner}/{name}/spec", (string owner, string name, string? revision, MarketplaceService marketplace)
            => marketplace.Spec(PackageId.Create(owner, name), revision));
        packages.MapPost("/{owner}/{name}/verify", (string owner, string name, string? revision, MarketplaceService marketplace)
            => marketplace.Verify(PackageId.Create(owner, name), revision));
        packages.MapPost("/{owner}/{name}/publish", (string owner, string name, PublishPackageRequest request, PackageService service)
            => service.Publish(PackageId.Create(owner, name), request));

        routes.MapPost("/workspaces/{workspaceId}/csharp/{id}/share", (string workspaceId, string id, ShareCSharpRequest request, CSharpSharing sharing, CancellationToken ct)
                => sharing.Share(workspaceId, id, request, ct))
            .AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync)
            .AddEndpointFilter(Guard);

        var installed = routes.MapGroup("/workspaces/{workspaceId}/packages/{owner}/{name}")
            .AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync)
            .AddEndpointFilter(Guard);
        installed.MapGet("", (string workspaceId, string owner, string name, PackageService service)
            => service.ReadApp(workspaceId, PackageId.Create(owner, name)));
        installed.MapGet("/accounts", (string workspaceId, string owner, string name, string? revision, PackageService service)
            => service.AccountOptions(workspaceId, PackageId.Create(owner, name), revision));
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
        installed.MapGet("/invocations/{invocationId:guid}/discussion", (string workspaceId, string owner, string name, Guid invocationId, PackageService service, MarketplaceService marketplace)
            => marketplace.Discussion(service.AppKey(workspaceId, PackageId.Create(owner, name)), invocationId));
        installed.MapGet("/invocations/{invocationId:guid}", (string workspaceId, string owner, string name, Guid invocationId, PackageService service)
            => service.ReadInvocation(workspaceId, PackageId.Create(owner, name), invocationId));
    }

    // A draft belongs to whoever is signed in, and the app it builds is published under their name.
    private static IAppDraft Draft(IDigitalBrain brain, string id)
    {
        if (!Guid.TryParse(id, out var draftId)) { throw new ArgumentException("A draft id is a GUID."); }
        return brain.Get<IAppDraft>($"{CallerContextStamper.Require().PrincipalId}/drafts/{draftId:N}");
    }

    // Packages run code, so they share the C# console's developer-mode gate.
    private static async ValueTask<object?> Guard(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
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
