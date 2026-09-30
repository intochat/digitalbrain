using DigitalBrain.Assistant;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Assistant.Tests.Unit;

public sealed class AgentRouteFacts
{
    [Fact]
    public void ConversationsVoiceAndInstalledPackagesAreServedUnderTheBrainRouteAndRegistryStaysGlobal()
    {
        var snapshot = RouteSnapshot.Map(services =>
        {
            services.AddSingleton(typeof(IDigitalBrain), _ => null!);
            services.AddSingleton(typeof(PackageService), _ => null!);
        }, app => { AgentEndpoints.Map(app); PackageEndpoints.Map(app); });

        snapshot.AssertEveryMethodAndRouteIsDistinct();
        var routes = snapshot.Routes;
        Assert.Contains("/brains/{brainId}/conversations/{threadId}", routes);
        Assert.Contains("/brains/{brainId}/voice", routes);
        Assert.Contains("/brains/{brainId}/packages/{owner}/{name}/invocations", routes);
        Assert.Contains("/brains/{brainId}/built-in/settings/open", routes);
        Assert.Contains("/packages/{owner}/{name}/publish", routes);
        Assert.Contains("/agent", routes);
        Assert.Contains("/ai/models", routes);
        Assert.DoesNotContain(routes, route => route.StartsWith("/workspaces", StringComparison.Ordinal));
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
        public ValueTask<bool> CanAccessAsync(string principalId, string brainId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(principalId == "alice");
    }
}
