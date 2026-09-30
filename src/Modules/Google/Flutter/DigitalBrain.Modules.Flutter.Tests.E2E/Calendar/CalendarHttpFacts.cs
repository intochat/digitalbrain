using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Calendar;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Calendar;

public sealed class CalendarHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule, FlutterModuleOptions>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        await brain.Get<ICalendar>(UiScope.Key(BrainScope.Create("owner", "workspace-a").Id, "cal")).Set("day", ["2026-09-20"]);
        var state = await brain.HttpClient.GetFromJsonAsync<CalendarState>("/brains/workspace-a/ui/calendars/cal", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("day", state!.Mode);
    }
}
