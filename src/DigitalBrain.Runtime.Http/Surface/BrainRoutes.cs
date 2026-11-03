using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Runtime.Http.Surface;

public static class BrainRoutes
{
    public static RouteGroupBuilder Group(IEndpointRouteBuilder endpoints, string suffix = "")
        => BrainRouteGroups.Brain(endpoints, suffix);

    public static RouteGroupBuilder SessionGroup(IEndpointRouteBuilder endpoints, string suffix = "")
        => BrainRouteGroups.Session(endpoints, suffix);
}
