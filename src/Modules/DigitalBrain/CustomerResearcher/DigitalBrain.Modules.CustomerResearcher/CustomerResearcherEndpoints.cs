using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.CustomerResearcher;

internal static class CustomerResearcherEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var applications = BrainRoutes.Group(endpoints, "/applications");
        applications.MapPost("/customer-researcher/open", async (IDigitalBrain brain) =>
        {
            var window = await brain.Get<ICustomerResearcher>(CustomerResearcherSurface.Key(BrainScope.CurrentId())).OpenWindow();
            return Results.Ok(new { id = window.Id, title = window.Title, kind = "surface", surface = window.Surface });
        });
    }
}
