using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Rating;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class RatingFacts
{
    [Fact]
    public async Task SetWritesValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<IRating>("stars").Set(5, 4);
        Assert.Equal(4, (await brain.Get<IRating>("stars").Read()).Value);
    }
}
