using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orleans;

namespace DigitalBrain.Sdk;

public static class WebhookExtensions
{
    public static RouteHandlerBuilder MapJsonWebhook<TBody>(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<TBody, IGrainFactory, CancellationToken, Task<IResult>> handle)
        => endpoints.MapPost(pattern, handle);
}
