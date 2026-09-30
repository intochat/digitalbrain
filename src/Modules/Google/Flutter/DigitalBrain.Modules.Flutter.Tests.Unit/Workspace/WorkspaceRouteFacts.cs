using DigitalBrain.Contracts;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Testing.Unit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Flutter.Tests.Workspace;

public sealed class WorkspaceRouteFacts
{
    [Fact]
    public void WindowStateEventsAndReportsAreServedOnlyUnderTheBrainRoute()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(typeof(IDigitalBrain), _ => null!);
        IEndpointRouteBuilder app = builder.Build();
        WorkspaceEndpoints.Map(app);

        var routes = app.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText!).ToArray();

        Assert.Contains("/brains/{brainId}/", routes);
        Assert.Contains("/brains/{brainId}/windows/{windowId}/close", routes);
        Assert.Contains("/brains/{brainId}/events", routes);
        Assert.Contains("/brains/{brainId}/reports", routes);
        Assert.All(routes, route => Assert.StartsWith("/brains/{brainId}", route));
    }

    [Fact]
    public async Task AReportKeepsTheIntentIdAndSurvivesReactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        var reports = brain.Get<IProblemReports>("workspace-scope-1");

        var report = await reports.Add("workspace-1", "run-42", "The table did not open.");
        await brain.DeactivateAsync(reports, ct);

        Assert.Equal("run-42", report.IntentId);
        Assert.Equal(report, Assert.Single(await reports.Read()));
    }

    [Fact]
    public async Task AnEmptyIntentIdIsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            brain.Get<IProblemReports>("workspace-scope-1").Add("workspace-1", "  ", "something broke"));
    }
}
