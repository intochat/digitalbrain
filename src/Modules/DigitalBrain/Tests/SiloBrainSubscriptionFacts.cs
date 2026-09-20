using DigitalBrain.Testing.Unit;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SiloBrainSubscriptionFacts
{
    [Fact]
    public async Task SiloHostedBrainClientReceivesPublication()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        var listener = brain.Get<ISiloBrainListener>("listener");
        Assert.True(await listener.HubIsPresent());
        await listener.Listen("silo-source");
        var source = brain.Get<ITestEmitter>("silo-source");
        await source.Emit(42);
        var last = await TestWait.UntilAsync(_ => listener.Last(), value => value == 42, TimeSpan.FromSeconds(5), ct);
        Assert.Equal(42, last);
    }
}
