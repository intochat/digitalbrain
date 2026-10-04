using DigitalBrain.Testing;
using DigitalBrain.Testing.Module;
using Xunit;

namespace DigitalBrain.Client.Orleans.Tests.Unit;

public sealed class SiloBrainSubscriptionFacts
{
    [Fact]
    public async Task SiloHostedBrainClientReceivesPublication()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);
        var listener = brain.Get<ISiloBrainListener>("listener");
        Assert.True(await listener.HubIsPresent());
        await listener.Listen("silo-source");
        var source = brain.Get<ITestEmitter>("silo-source");
        await source.Emit(42);
        var last = await TestWait.UntilAsync(_ => listener.Last(), value => value == 42, TimeSpan.FromSeconds(5), ct);
        Assert.Equal(42, last);
    }

    [Fact]
    public async Task SiloHostedSubscriptionEndsWhenItsTokenIsCanceled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);

        Assert.True(await brain.Get<ISiloBrainListener>("canceling-listener").CancelingTheTokenEndsASubscription("canceled-source"));
    }
}
