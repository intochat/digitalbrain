using DigitalBrain.SmartPrompt;
using Xunit;

namespace DigitalBrain.Simulation.Tests.SmartPrompt;

[Collection(SimulationCollection.Name)]
public sealed class BehaviorScaleTests(SimulationFixture fixture)
{
    [Fact]
    public async Task Five_thousand_observers_share_one_trigger_directory_and_bounded_partitions()
    {
        var directory = fixture.Sim.Grains.GetGrain<IBehaviorTriggerDirectory>("x.post/account:elonmusk");
        var before = await directory.ReadStats();
        for (var index = 0; index < 5000; index++)
        {
            await directory.Subscribe(new BehaviorSubscription(
                $"owner-{index}",
                "bitcoin-tracker",
                "Track Elon posts about Bitcoin",
                $"revision-{index}"));
        }

        var stats = await directory.ReadStats();

        Assert.Equal(5000, stats.SubscriptionCount - before.SubscriptionCount);
        Assert.InRange(stats.ActivePartitions, 2, BehaviorRouting.PartitionCount);
        Assert.Equal(BehaviorRouting.PartitionCount, stats.ConfiguredPartitions);
    }
}
