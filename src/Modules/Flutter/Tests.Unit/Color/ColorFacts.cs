using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Color;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ColorFacts
{
    [Fact]
    public async Task SetNormalizesHex()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<IColor>("accent").Set("#0a84ff");
        Assert.Equal("#0A84FF", (await brain.Get<IColor>("accent").Read()).Hex);
    }
}
