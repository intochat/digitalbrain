using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Assistant.Tests;

public sealed class AgentRouteFacts
{
    [Fact]
    public void ConversationsVoiceAndInstalledPackagesAreServedUnderTheBrainRouteAndRegistryStaysGlobal()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(typeof(IDigitalBrain), _ => null!);
        builder.Services.AddSingleton(typeof(PackageService), _ => null!);
        IEndpointRouteBuilder app = builder.Build();
        AgentEndpoints.Map(app);
        PackageEndpoints.Map(app);

        var routes = app.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText!).ToArray();

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
    public async Task TheAgentRouteDecidesMembershipFromTheBodyBrain()
    {
        var access = new AliceOwnsHerBrain();
        var alicesBrain = "research";

        var bob = await Resolve(access, "bob", alicesBrain);
        Assert.Equal(StatusCodes.Status403Forbidden, ((IStatusCodeHttpResult)bob.Denied!).StatusCode);
        Assert.Null(bob.Scope);

        var alice = await Resolve(access, "alice", alicesBrain);
        Assert.Null(alice.Denied);
        Assert.Equal(BrainScope.Create("alice", alicesBrain).Id, alice.Scope!.Id);
    }

    [Fact]
    public async Task TheBodyBrainWinsOverTheBrainTheCallerWasStampedWith()
    {
        var resolved = await Resolve(new AliceOwnsHerBrain(), "alice", "research", stampedBrain: "personal");

        Assert.Equal(BrainScope.Create("alice", "research").Id, resolved.Scope!.Id);
        Assert.NotEqual(BrainScope.Create("alice", "personal").Id, resolved.Scope.Id);
    }

    private static async Task<(BrainScope? Scope, IResult? Denied)> Resolve(IBrainAccess access, string principal, string bodyBrain, string stampedBrain = "personal")
    {
        var services = new ServiceCollection().AddSingleton(access).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Headers.Authorization = "Basic credentials";
        CallerContextStamper.Stamp(new CallerContext
        {
            PrincipalId = principal, AccountId = principal, BrainId = stampedBrain,
            Kind = CallerKind.User, StampedBy = TrustedEdge.AuthenticatedHttp,
        });
        return await AgentEndpoints.ResolveBodyBrain(http, bodyBrain);
    }

    private sealed class AliceOwnsHerBrain : IBrainAccess
    {
        public ValueTask<bool> CanAccessAsync(string principalId, string brainId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(principalId == "alice");
    }
}
