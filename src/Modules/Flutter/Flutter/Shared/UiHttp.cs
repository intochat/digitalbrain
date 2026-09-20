using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Flutter;

internal static class UiHttp
{
    public static void MapGet<TNeuron, TState>(IEndpointRouteBuilder endpoints, string collection, Func<TNeuron, Task<TState>> read)
        where TNeuron : class, IGrainWithStringKey
    {
        endpoints.MapGet($"/ui/{collection}/{{name}}", async Task<IResult> (string name, IGrainFactory grains, CancellationToken cancellationToken) =>
            Results.Ok(await read(grains.GetGrain<TNeuron>(name)).WaitAsync(cancellationToken)));
    }

    public static void MapPost(IEndpointRouteBuilder endpoints, string template, Delegate handler)
        => endpoints.MapPost("/ui/" + template, handler);
}
