using DigitalBrain;
using DigitalBrain.Assistant;
using DigitalBrain.Compute;
using DigitalBrain.Compute.Usage;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Testing.Module;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Assistant.Tests.Unit;

public sealed class ComputeUsageRouteFacts
{
    [Fact]
    public async Task UsageRejectsZeroLimitsAndAnotherBrainsCursor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().WithModule<ComputeModule>().StartAsync(ct);
        await brain.Get<DigitalBrain.Platform.Contracts.Identity.IIdentityDirectory>(DigitalBrain.Platform.Contracts.Identity.IdentityGrains.Directory)
            .ShareBrainAsync("account", "own", "owner", "Owner", DigitalBrain.Platform.Contracts.Identity.MemberRole.Owner, ct);
        var store = brain.SiloServices.GetRequiredService<IUsageStore>();
        await store.AppendAsync("account", "foreign", "first", "{}", ct);
        await store.AppendAsync("account", "foreign", "second", "{}", ct);
        var cursor = (await store.ReadAsync("account", "foreign", 1, null, ct)).NextCursor;
        Assert.NotNull(cursor);
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IDigitalBrain>(brain.SiloServices.GetRequiredService<IDigitalBrain>());
        builder.Services.AddSingleton(brain.SiloServices.GetRequiredService<IBrainAccess>());
        builder.Services.AddSingleton(store);
        await using var app = builder.Build();
        new AssistantModule().Configure(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Single(route => route.RoutePattern.RawText == "/brains/{brainId}/compute/usage");
        CallerContextStamper.Stamp(new CallerContext
        {
            PrincipalId = "owner",
            AccountId = "account",
            BrainId = "own",
            Kind = CallerKind.User,
            StampedBy = TrustedEdge.AuthenticatedHttp,
        });
        foreach (var query in new[] { "?limit=0", "?cursor=" + Uri.EscapeDataString(cursor) })
        {
            var context = new DefaultHttpContext { RequestServices = app.Services, RequestAborted = ct };
            context.Request.Method = "GET";
            context.Request.QueryString = new QueryString(query);
            context.Request.RouteValues["brainId"] = "own";
            await endpoint.RequestDelegate!(context);
            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }
    }
}
