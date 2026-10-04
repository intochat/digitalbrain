using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Calendar;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Calendar;

[Collection(FlutterHostCollection.Name)]
public sealed class CalendarHttpFacts(FlutterHostFixture host)
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = host.Brain;
        var ws = host.Workspace();
        await brain.Get<ICalendar>(UiScope.Key(BrainScope.Create("owner", ws).Id, "cal")).Set("day", ["2026-09-20"]);
        var state = await brain.HttpClient.GetFromJsonAsync<CalendarState>($"/brains/{ws}/ui/calendars/cal", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("day", state!.Mode);
    }
}
