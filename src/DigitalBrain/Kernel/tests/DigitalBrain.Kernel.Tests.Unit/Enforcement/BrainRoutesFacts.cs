using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.Kernel.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Kernel.Tests.Unit.Enforcement;

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AGroupedEndpointDeniesAStampedCallerWhoIsNotAMember(bool authorized)
    {
        var (status, handlerRan) = await Invoke("personal", authorized: authorized, stamped: true);

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.False(handlerRan);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AGroupedEndpointRejectsARequestWithoutAStamp(bool authorized)
    {
        var (status, handlerRan) = await Invoke("personal", authorized: authorized, stamped: false);

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.False(handlerRan);
    }

    [Fact]
    public async Task AGroupedEndpointRejectsAnInvalidBrainIdBeforeMembershipIsChecked()
    {
        var (status, handlerRan) = await Invoke("..%2Fescape/", authorized: true, stamped: true);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.False(handlerRan);
    }

    private static async Task<(int Status, bool HandlerRan)> Invoke(string brainId, bool authorized, bool stamped)
    {
        var builder = WebApplication.CreateBuilder();
        DigitalBrain.Testing.TestLogging.Apply(builder.Configuration);
        builder.Services.AddSingleton<IBrainAccess, DenyEveryone>();
        IEndpointRouteBuilder app = builder.Build();
        var handlerRan = false;
        BrainRoutes.Group(app).MapGet("/ping", () => { handlerRan = true; return Results.Ok(); });
        var endpoint = app.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Single();

        if (stamped)
        {
            CallerContextStamper.Stamp(new CallerContext
            {
                PrincipalId = "intruder",
                AccountId = "intruder",
                BrainId = brainId,
                Kind = CallerKind.User,
                StampedBy = TrustedEdge.AuthenticatedHttp,
            });
        }

        var http = new DefaultHttpContext { RequestServices = ((IApplicationBuilder)app).ApplicationServices };
        http.Request.RouteValues["brainId"] = brainId;
        if (authorized) { http.Request.Headers.Authorization = "Basic abc"; }
        http.Response.Body = new MemoryStream();
        http.SetEndpoint(endpoint);

        await endpoint.RequestDelegate!(http);
        return (http.Response.StatusCode, handlerRan);
    }

    private sealed class DenyEveryone : IBrainAccess
    {
        public ValueTask<bool> CanAccessAsync(string principalId, string accountId, string brainId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }
}
