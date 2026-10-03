using System.Text;
using DigitalBrain;
using DigitalBrain.Assistant;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Assistant.Tests.Unit;

public sealed class AgentRouteFacts
{
    [Fact]
    public void AssistantDoesNotOwnAppOrPackageRoutes()
    {
        var snapshot = RouteSnapshot.Map(services =>
        {
            services.AddSingleton(typeof(IDigitalBrain), _ => null!);
            services.AddSingleton<DigitalBrain.Compute.Usage.IUsageStore>(_ => null!);
        }, app => new AssistantModule().Configure(app));
        Assert.DoesNotContain(snapshot.Routes, route => route.Contains("/packages", StringComparison.Ordinal) || route.Contains("/apps", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheAgentRouteDecidesMembershipFromTheBodyBrainAndIgnoresTheStampedBrain()
    {
        var access = new AliceOwnsHerBrain();
        var alicesBrain = "research";

        var bob = await Resolve(access, "bob", alicesBrain);
        Assert.Equal(StatusCodes.Status403Forbidden, ((IStatusCodeHttpResult)bob.Denied!).StatusCode);
        Assert.Null(bob.Scope);

        var alice = await Resolve(access, "alice", alicesBrain, stampedBrain: "personal");
        Assert.Null(alice.Denied);
        Assert.Equal(BrainScope.Create("alice", alicesBrain).Id, alice.Scope!.Id);
        Assert.NotEqual(BrainScope.Create("alice", "personal").Id, alice.Scope.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData(@"a")]
    public void AnAbsentOrInvalidBodyBrainIsRejectedByTheValidationTheHandlerUses(string? brainId)
        => Assert.False(BrainScope.IsValidId(brainId));

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"a/b\"")]
    [InlineData("\"a\\b\"")]
    public async Task TheAgentEndpointAnswers400ForAnAbsentOrInvalidBodyBrain(string brainIdJson)
    {
        var snapshot = RouteSnapshot.Map(services => services.AddSingleton(typeof(IDigitalBrain), _ => null!), AgentEndpoints.Map);
        var agent = snapshot.Endpoints.Single(endpoint => endpoint.RoutePattern.RawText == "/agent");
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().AddSingleton(typeof(IDigitalBrain), _ => null!).BuildServiceProvider() };
        http.Request.ContentType = "application/json";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            $$"""{"brainId":{{brainIdJson}},"threadId":"t","runId":"r","messages":[{"role":"user","content":"hi"}]}"""));

        await agent.RequestDelegate!(http);

        Assert.Equal(StatusCodes.Status400BadRequest, http.Response.StatusCode);
    }

    private static async Task<(BrainScope? Scope, IResult? Denied)> Resolve(IBrainAccess access, string principal, string bodyBrain, string stampedBrain = "personal")
    {
        var services = new ServiceCollection().AddSingleton(access).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Headers.Authorization = "Basic credentials";
        CallerContextStamper.Stamp(new CallerContext
        {
            PrincipalId = principal,
            AccountId = principal,
            BrainId = stampedBrain,
            Kind = CallerKind.User,
            StampedBy = TrustedEdge.AuthenticatedHttp,
        });
        return await AgentEndpoints.ResolveBodyBrain(http, bodyBrain);
    }

    private sealed class AliceOwnsHerBrain : IBrainAccess
    {
        public ValueTask<bool> CanAccessAsync(string principalId, string accountId, string brainId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(principalId == "alice");
    }
}
