using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Flutter;

// UI-kit neurons hold owner/workspace window data, so every route is addressed under a
// workspace scope and the grain key carries that scope (C06 / P1.7).
public static class UiScope
{
    public static string Key(string workspace, string name) => $"{workspace}:{name}";
}

internal static class UiHttp
{
    public static void MapGet<TNeuron, TState>(IEndpointRouteBuilder endpoints, string collection, Func<TNeuron, Task<TState>> read)
        where TNeuron : class, IGrainWithStringKey
    {
        endpoints.MapGet($"/workspaces/{{workspace}}/ui/{collection}/{{name}}",
            async Task<IResult> (string workspace, string name, IGrainFactory grains, CancellationToken cancellationToken) =>
                Results.Ok(await read(grains.GetGrain<TNeuron>(UiScope.Key(workspace, name))).WaitAsync(cancellationToken)));
    }

    public static void MapPost(IEndpointRouteBuilder endpoints, string template, Delegate handler)
        => endpoints.MapPost($"/workspaces/{{workspace}}/ui/{template}", handler)
            .AddEndpointFilter(static async (context, next) =>
            {
                var workspace = context.HttpContext.Request.RouteValues["workspace"]?.ToString()
                    ?? throw new ArgumentException("A workspace scope is required.");
                var name = context.HttpContext.Request.RouteValues["name"]?.ToString()
                    ?? throw new ArgumentException("A UI value name is required.");
                context.Arguments[0] = UiScope.Key(workspace, name);
                return await next(context);
            });
}