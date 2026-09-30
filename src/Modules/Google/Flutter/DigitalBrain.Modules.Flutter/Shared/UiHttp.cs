using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Flutter;

// UI-kit neurons hold per-brain window data, so every route is addressed under a
// brain scope and the grain key carries that scope (C06 / P1.7).
public static class UiScope
{
    public static string Key(string scope, string name) => $"{scope}:{name}";
}

internal static class UiHttp
{
    public static void MapGet<TNeuron, TState>(IEndpointRouteBuilder endpoints, string collection, Func<TNeuron, Task<TState>> read)
        where TNeuron : class, IGrainWithStringKey
    {
        BrainRoutes.Group(endpoints, "/ui").MapGet($"/{collection}/{{name}}",
            async Task<IResult> (string name, IGrainFactory grains, CancellationToken cancellationToken) =>
                Results.Ok(await read(grains.GetGrain<TNeuron>(UiScope.Key(BrainScope.CurrentId(), name))).WaitAsync(cancellationToken)));
    }

    public static void MapPost(IEndpointRouteBuilder endpoints, string template, Delegate handler)
        => BrainRoutes.Group(endpoints, "/ui").MapPost("/" + template, handler)
            .AddEndpointFilter(static async (context, next) =>
            {
                var name = context.HttpContext.Request.RouteValues["name"]?.ToString()
                    ?? throw new ArgumentException("A UI value name is required.");
                context.Arguments[0] = UiScope.Key(BrainScope.CurrentId(), name);
                return await next(context);
            });
}
