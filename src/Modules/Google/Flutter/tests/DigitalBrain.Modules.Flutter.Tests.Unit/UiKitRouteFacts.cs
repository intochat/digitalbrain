using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Flutter.Tests.Unit;

public sealed class UiKitRouteFacts
{
    [Fact]
    public void EveryUiKitRouteIsServedExactlyOnceUnderTheBrainRoute()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(typeof(IDigitalBrain), _ => null!);
        builder.Services.AddSingleton(typeof(IGrainFactory), _ => null!);
        IEndpointRouteBuilder app = builder.Build();
        app.MapUiKit();

        var routes = app.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => $"{string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {endpoint.RoutePattern.RawText}").Order(StringComparer.Ordinal).ToArray();

        Assert.All(routes, route => Assert.Contains(" /brains/{brainId}/", route));
        Assert.Equal(
        [
        "GET /brains/{brainId}/ui/browsers/{name}",
        "GET /brains/{brainId}/ui/buttons/{name}",
        "GET /brains/{brainId}/ui/calendars/{name}",
        "GET /brains/{brainId}/ui/cards/{name}",
        "GET /brains/{brainId}/ui/charts/{name}",
        "GET /brains/{brainId}/ui/clocks/{name}",
        "GET /brains/{brainId}/ui/collections/{name}",
        "GET /brains/{brainId}/ui/colors/{name}",
        "GET /brains/{brainId}/ui/expanders/{name}",
        "GET /brains/{brainId}/ui/forms/{name}",
        "GET /brains/{brainId}/ui/graphs/{name}",
        "GET /brains/{brainId}/ui/imagecanvases/{name}",
        "GET /brains/{brainId}/ui/images/{name}",
        "GET /brains/{brainId}/ui/infobars/{name}",
        "GET /brains/{brainId}/ui/layouts/{name}",
        "GET /brains/{brainId}/ui/maps/{name}",
        "GET /brains/{brainId}/ui/people/{name}",
        "GET /brains/{brainId}/ui/progress/{name}",
        "GET /brains/{brainId}/ui/ratings/{name}",
        "GET /brains/{brainId}/ui/sheets/{name}",
        "GET /brains/{brainId}/ui/sliders/{name}",
        "GET /brains/{brainId}/ui/surfaces/{name}",
        "GET /brains/{brainId}/ui/tables/{name}",
        "GET /brains/{brainId}/ui/tabs/{name}",
        "GET /brains/{brainId}/ui/texts/{name}",
        "GET /brains/{brainId}/ui/toggles/{name}",
        "GET /brains/{brainId}/ui/trees/{name}",
        "GET /brains/{brainId}/ui/videos/{name}",
        "POST /brains/{brainId}/ui/browsers/{name}/navigate",
        "POST /brains/{brainId}/ui/buttons/{name}/click",
        "POST /brains/{brainId}/ui/buttons/{name}/set",
        "POST /brains/{brainId}/ui/calendars/{name}",
        "POST /brains/{brainId}/ui/cards/{name}",
        "POST /brains/{brainId}/ui/charts/{name}/append",
        "POST /brains/{brainId}/ui/charts/{name}/render",
        "POST /brains/{brainId}/ui/clocks/{name}",
        "POST /brains/{brainId}/ui/colors/{name}",
        "POST /brains/{brainId}/ui/expanders/{name}",
        "POST /brains/{brainId}/ui/expanders/{name}/toggle",
        "POST /brains/{brainId}/ui/forms/{name}/draft",
        "POST /brains/{brainId}/ui/forms/{name}/submit",
        "POST /brains/{brainId}/ui/graphs/{name}/render",
        "POST /brains/{brainId}/ui/images/{name}",
        "POST /brains/{brainId}/ui/infobars/{name}/dismiss",
        "POST /brains/{brainId}/ui/infobars/{name}/show",
        "POST /brains/{brainId}/ui/maps/{name}",
        "POST /brains/{brainId}/ui/people/{name}",
        "POST /brains/{brainId}/ui/progress/{name}",
        "POST /brains/{brainId}/ui/ratings/{name}",
        "POST /brains/{brainId}/ui/sheets/{name}",
        "POST /brains/{brainId}/ui/sliders/{name}/configure",
        "POST /brains/{brainId}/ui/sliders/{name}/value",
        "POST /brains/{brainId}/ui/tables/{name}/replace",
        "POST /brains/{brainId}/ui/tables/{name}/view",
        "POST /brains/{brainId}/ui/tabs/{name}",
        "POST /brains/{brainId}/ui/tabs/{name}/select",
        "POST /brains/{brainId}/ui/textfields/{name}/configure",
        "POST /brains/{brainId}/ui/textfields/{name}/value",
        "POST /brains/{brainId}/ui/texts/{name}",
        "POST /brains/{brainId}/ui/toggles/{name}/flip",
        "POST /brains/{brainId}/ui/toggles/{name}/set",
        "POST /brains/{brainId}/ui/trees/{name}",
        "POST /brains/{brainId}/ui/trees/{name}/select",
        "POST /brains/{brainId}/ui/videos/{name}/load",
        "POST /brains/{brainId}/ui/videos/{name}/pause",
        "POST /brains/{brainId}/ui/videos/{name}/play",
        "POST /brains/{brainId}/ui/videos/{name}/seek",
        ], routes);
    }
}
