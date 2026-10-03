using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.Sdk.Http;
using DigitalBrain.Apps;
using DigitalBrain.Kernel.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Apps;

internal static class PackageEndpoints
{
    public static void MapAuthoring(RouteGroupBuilder registry)
    {
        registry.MapGet("", ([global::Microsoft.AspNetCore.Mvc.FromServices] AppService service) => service.List());
        registry.MapGet("/{owner}/{name}", (string owner, string name, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service) => service.Read(PackageId.Create(owner, name)));
        registry.MapGet("/{owner}/{name}/revisions/{revision}", (string owner, string name, string revision, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.ReadRevision(PackageId.Create(owner, name), revision));
        registry.MapPost("/{owner}/{name}/revisions", (string owner, string name, CommitAppRequest request, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.Commit(PackageId.Create(owner, name), request));
        registry.MapPost("/{owner}/{name}/fork", (string owner, string name, ForkAppRequest request, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.Fork(PackageId.Create(owner, name), request));
        registry.MapPost("/{owner}/{name}/pull", (string owner, string name, PullAppRequest request, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.Pull(PackageId.Create(owner, name), request));
        registry.MapPost("/{owner}/{name}/proposals", (string owner, string name, ProposeAppRequest request, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.Propose(PackageId.Create(owner, name), request));
        registry.MapPost("/{owner}/{name}/proposals/{number:int}/accept", (string owner, string name, int number, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.Accept(PackageId.Create(owner, name), number));
        registry.MapPost("/{owner}/{name}/proposals/{number:int}/close", (string owner, string name, int number, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.Close(PackageId.Create(owner, name), number));
        registry.MapPost("/{owner}/{name}/publish", (string owner, string name, PublishAppRequest request, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.Publish(PackageId.Create(owner, name), request));

    }

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var installed = BrainRoutes.Group(endpoints, "/packages/{owner}/{name}").AddEndpointFilter(ModuleRouteGuard.Guard);
        installed.MapGet("", (string owner, string name, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.ReadApp(PackageId.Create(owner, name)));
        installed.MapGet("/accounts", (string owner, string name, string? revision, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.AccountOptions(PackageId.Create(owner, name), revision));
        installed.MapPost("", (string owner, string name, InstallAppRequest request, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.Install(PackageId.Create(owner, name), request));
        installed.MapPost("/configure", (string owner, string name, ConfigureAppRequest request, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.Configure(PackageId.Create(owner, name), request));
        installed.MapPost("/upgrade", (string owner, string name, UpgradeAppRequest request, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.Upgrade(PackageId.Create(owner, name), request));
        installed.MapDelete("", (string owner, string name, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.Uninstall(PackageId.Create(owner, name)));
        installed.MapPost("/abandon-storage", (string owner, string name, AbandonAppStorage request, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.AbandonStorage(PackageId.Create(owner, name), request));
        installed.MapPost("/invocations", (string owner, string name, InvokeAppRequest request, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.Invoke(PackageId.Create(owner, name), request));
        installed.MapGet("/invocations/{invocationId:guid}", (string owner, string name, Guid invocationId, [global::Microsoft.AspNetCore.Mvc.FromServices] AppService service)
            => service.ReadInvocation(PackageId.Create(owner, name), invocationId));

        BuiltInSettingsEndpoints.Map(endpoints);
    }
}


