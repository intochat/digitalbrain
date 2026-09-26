using DigitalBrain.Compute;
using DigitalBrain.Contracts.Enforcement;
using IntoChat.Apps;
using Xunit;

namespace IntoChat.Tests.Unit;

public sealed class WebResearchFacts
{
    [Fact]
    public async Task EachRequestRecordsOneMeterEvent()
    {
        var sink = new CapturingWebResearchMeterSink();
        var research = new WebResearchService(new DeterministicWebResearchProvider(), sink, TimeProvider.System);
        var caller = new CallerContext
        {
            PrincipalId = "app-principal",
            AccountId = "owner-1",
            WorkspaceId = "owner-1",
            Kind = CallerKind.App,
            StampedBy = TrustedEdge.AppProxy,
            AppId = "leadgenerator",
            IntentId = "intent-1",
        };
        var ct = TestContext.Current.CancellationToken;

        await research.SearchAsync(new WebResearchQuery("acme"), caller, ct);
        await research.BrowseAsync(new BrowseWebRequest("https://acme.test"), caller, ct);
        await research.LookupCompanyAsync(new CompanyLookup("Acme"), caller, ct);

        Assert.Equal(3, sink.Events.Count);
        Assert.All(sink.Events, meter =>
        {
            Assert.Equal(WebResearchMeters.SearchRequest, meter.MeterId);
            Assert.Equal(WebResearchMeters.RequestUnit, meter.Unit);
            Assert.Equal(caller.IntentId, meter.IntentId);
            Assert.Equal(caller.WorkspaceId, meter.WorkspaceId);
        });
    }
}

internal sealed class CapturingWebResearchMeterSink : IMeterSink
{
    public List<MeterEvent> Events { get; } = [];

    public ValueTask RecordAsync(MeterEvent meterEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(meterEvent);
        return ValueTask.CompletedTask;
    }
}
