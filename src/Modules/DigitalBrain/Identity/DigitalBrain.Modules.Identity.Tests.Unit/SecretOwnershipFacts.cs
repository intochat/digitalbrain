using System.Text;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Platform.Secrets;
using DigitalBrain.Testing.Unit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Identity.Tests.Unit;

public sealed class SecretOwnershipFacts
{
    [Fact]
    public async Task AnotherOwnersSecretIsForbiddenWhateverThePathSays()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(brain.Grains);
        builder.Services.AddSingleton<DigitalBrain.Contracts.IDigitalBrain>(brain);
        await using var app = builder.Build();
        DigitalBrain.Platform.PlatformHosting.MapDigitalBrainPlatform(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Single(route => route.RoutePattern.RawText == "/secrets/{owner}");
        CallerContextStamper.Stamp(new CallerContext
        {
            PrincipalId = "owner", AccountId = "account", BrainId = "brain",
            Kind = CallerKind.User, StampedBy = TrustedEdge.AuthenticatedHttp,
        });
        var context = new DefaultHttpContext { RequestServices = app.Services, RequestAborted = ct };
        context.Request.Method = "POST";
        context.Request.RouteValues["owner"] = "someone-else";
        context.Request.ContentType = "application/json";
        var body = Encoding.UTF8.GetBytes("""{"name":"me.apiKey","label":"API key","value":"canary"}""");
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBody());
        await endpoint.RequestDelegate!(context);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    private sealed class RequestBody : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
