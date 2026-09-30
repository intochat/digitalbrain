using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DigitalBrain.Runtime.Tests.Unit.Enforcement;

public sealed class BrainRoutesFacts
{
    [Fact]
    public async Task AnInvalidBrainIdIsRejectedBeforeAnyGrainCall()
    {
        var http = new DefaultHttpContext();
        http.Request.RouteValues["brainId"] = "..%2Fescape/";

        var result = await BrainRoutes.ValidateId(http);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result!).StatusCode);
    }

    [Fact]
    public async Task AnUnstampedAuthenticatedRequestIsUnauthorized()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Basic abc";

        var denied = await BrainAccessFilter.Decide(http, "personal");

        Assert.Equal(StatusCodes.Status401Unauthorized, ((IStatusCodeHttpResult)denied!).StatusCode);
    }
}
