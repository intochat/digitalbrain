using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Calendar;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CalendarFacts
{
    [Fact]
    public async Task SetWritesMode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.StartAsync(new() { Modules = [new DigitalBrain.Core.ModuleDefinition(typeof(FlutterModule))] }, ct);
        await brain.Get<ICalendar>("cal").Set("day", ["2026-09-20"]);
        Assert.Equal("day", (await brain.Get<ICalendar>("cal").Read()).Mode);
    }
}
