using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Testing.Module;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Flutter.Tests.Workspace;

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
            .OfType<RouteEndpoint>().Select(endpoint => $"{string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {endpoint.RoutePattern.RawText}").Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                "GET /brains/{brainId}/",
                "GET /brains/{brainId}/events",
                "POST /brains/{brainId}/connected-sources",
                "POST /brains/{brainId}/reports",
                "POST /brains/{brainId}/windows/{windowId}/close",
                "POST /brains/{brainId}/windows/{windowId}/reopen",
            ],
            routes);
    }

    [Fact]
    public async Task AReportKeepsTheIntentIdAndSurvivesReactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);
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
        await using var brain = await ModuleTest.Create().StartAsync(ct);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            brain.Get<IProblemReports>("workspace-scope-1").Add("workspace-1", "  ", "something broke"));
    }
}
