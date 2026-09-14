using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests.Slots;

public sealed class ActiveSlotLeaseFacts
{
    [Fact]
    public void A_host_without_slots_always_holds_the_lease()
    {
        var lease = new SingleSlotLease();
        Assert.True(lease.HoldsLease);
        Assert.Equal(string.Empty, lease.Slot);
        Assert.Equal(0, lease.Generation);
    }

    [Fact]
    public async Task A_single_slot_lease_cannot_hand_the_lease_to_another_slot()
    {
        var lease = new SingleSlotLease("a");
        Assert.True(await lease.TryAcquireAsync("a", TestContext.Current.CancellationToken));
        Assert.False(await lease.TryAcquireAsync("b", TestContext.Current.CancellationToken));
        Assert.True(lease.HoldsLease);
    }

    [Fact]
    public void The_other_slot_holding_the_lease_makes_this_one_standby()
    {
        var lease = new InMemoryActiveSlotLease("a", holder: "other");
        Assert.False(lease.HoldsLease);
        Assert.Equal("a", lease.Slot);
        lease.Holder = "a";
        Assert.True(lease.HoldsLease);
    }

    [Fact]
    public async Task Acquiring_names_the_new_owner_and_bumps_the_generation()
    {
        var lease = new InMemoryActiveSlotLease("a");
        var generation = lease.Generation;
        Assert.True(await lease.TryAcquireAsync("b", TestContext.Current.CancellationToken));
        Assert.Equal("b", lease.Holder);
        Assert.False(lease.HoldsLease);
        Assert.True(lease.Generation > generation);
    }

    [Fact]
    public async Task A_write_that_races_the_read_loses_the_compare_and_swap()
    {
        var lease = new InMemoryActiveSlotLease("a");
        // The table answers 412 UpdateConditionNotSatisfied here (spike S3); the fake models the same
        // rule with a generation counter, so a fact can lose the race on purpose.
        lease.Interleave = () => lease.Holder = "other";
        Assert.False(await lease.TryAcquireAsync("b", TestContext.Current.CancellationToken));
        Assert.Equal("other", lease.Holder);
    }

    [Fact]
    public async Task Releasing_only_gives_up_a_lease_this_slot_holds()
    {
        var lease = new InMemoryActiveSlotLease("a");
        await lease.ReleaseAsync(TestContext.Current.CancellationToken);
        Assert.Null(lease.Holder);
        lease.Holder = "other";
        await lease.ReleaseAsync(TestContext.Current.CancellationToken);
        Assert.Equal("other", lease.Holder);
    }

    [Fact]
    public async Task A_refresh_is_counted_so_a_fact_can_see_the_refresher_run()
    {
        var lease = new InMemoryActiveSlotLease("a");
        await lease.RefreshAsync(TestContext.Current.CancellationToken);
        await lease.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, lease.Refreshes);
    }

    [Fact]
    public void The_refusal_names_the_standby_slot()
    {
        var error = new StandbySlotException("b");
        Assert.Equal("b", error.Slot);
        Assert.Equal("standby slot: silo 'b' does not hold the active lease", error.Message);
    }

    [Fact]
    public void Minting_a_cluster_id_stamps_the_slot_with_the_given_instant()
    {
        var now = new DateTimeOffset(2026, 9, 14, 11, 21, 58, 337, TimeSpan.Zero);
        Assert.Equal("a-20260914112158337", ClusterIdMinting.Mint("a", now));
    }
}
