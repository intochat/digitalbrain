using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Clock;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Clock;

public sealed class ClockFacts
{
    [Fact]
    public async Task SetWritesLabelAndDueDate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<IClock>("tea").Set("Tea", DateTimeOffset.UnixEpoch);
        var state = await brain.Get<IClock>("tea").Read();
        Assert.Equal("Tea", state.Label);
        Assert.Equal(DateTimeOffset.UnixEpoch, state.DueAt);
    }
}
