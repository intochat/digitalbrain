using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Calendar;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Calendar;

[Collection(FlutterBackendCollection.Name)]
public sealed class CalendarHttpFacts(FlutterBackendFixture host)
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await host.LeaseAsync(ct);
        await brain.Get<ICalendar>(UiScope.Key(BrainScope.Create("owner", brain.WorkspaceId).Id, "cal")).Set("day", ["2026-09-20"]);
        var state = await brain.HttpClient.GetFromJsonAsync<CalendarState>($"/brains/{brain.WorkspaceId}/ui/calendars/cal", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("day", state!.Mode);
    }
}
