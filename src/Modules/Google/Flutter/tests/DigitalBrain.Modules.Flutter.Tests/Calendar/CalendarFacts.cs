using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Calendar;
using DigitalBrain.Testing.Module;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Calendar;

public sealed class CalendarFacts
{
    [Fact]
    public async Task SetWritesModeAndSelection()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<ICalendar>("cal").Set("day", ["2026-09-20"]);
        var state = await brain.Get<ICalendar>("cal").Read();
        Assert.Equal("day", state.Mode);
        Assert.Equal(["2026-09-20"], state.Selected);
    }
}
