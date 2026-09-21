using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Rating;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Rating;

public sealed class RatingFacts
{
    [Fact]
    public async Task SetWritesMaximumAndValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<IRating>("stars").Set(5, 4);
        var state = await brain.Get<IRating>("stars").Read();
        Assert.Equal(5, state.Max);
        Assert.Equal(4, state.Value);
    }
}
