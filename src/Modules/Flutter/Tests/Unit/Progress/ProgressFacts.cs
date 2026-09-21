using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Progress;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Progress;

public sealed class ProgressFacts
{
    [Fact]
    public async Task SetWritesValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<IProgress>("load").Set(true, 0.4, "loading");
        Assert.Equal(0.4, (await brain.Get<IProgress>("load").Read()).Value);
    }
}
