using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Calendar;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CalendarHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntegrationTest.Create().WithModule<FlutterModule>(flutter => flutter.WithoutHost())
            .StartAsync(ct);
        await brain.Get<ICalendar>("cal").Set("day", ["2026-09-20"]);
        var state = await brain.HttpClient.GetFromJsonAsync<CalendarState>("/ui/calendars/cal", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("day", state!.Mode);
    }
}
