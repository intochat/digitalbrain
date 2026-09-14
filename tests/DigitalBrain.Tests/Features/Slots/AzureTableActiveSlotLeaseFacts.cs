using Azure;
using Azure.Data.Tables;
using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Aspire;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Slots;

// The real lease against Azurite. Needs table storage, so it runs only when
// DIGITALBRAIN_SLOT_LEASE_TESTS=1; DIGITALBRAIN_SLOT_LEASE_TABLES overrides the connection string when
// Azurite runs on the AppHost's random ports instead of the development-storage defaults.
public sealed class AzureTableActiveSlotLeaseFacts : IAsyncDisposable
{
    private const string Skip = "Set DIGITALBRAIN_SLOT_LEASE_TESTS=1 (and DIGITALBRAIN_SLOT_LEASE_TABLES for a non-default Azurite) to exercise the real lease row.";

    public static bool LeaseTestsEnabled => Environment.GetEnvironmentVariable("DIGITALBRAIN_SLOT_LEASE_TESTS") == "1";

    private static string ConnectionString => Environment.GetEnvironmentVariable("DIGITALBRAIN_SLOT_LEASE_TABLES") is { Length: > 0 } configured
        ? configured
        : "UseDevelopmentStorage=true";

    private readonly TableServiceClient _tables = new(ConnectionString);

    [Fact(Skip = Skip, SkipUnless = nameof(LeaseTestsEnabled))]
    public async Task The_first_slot_to_bootstrap_owns_the_row()
    {
        await ResetAsync();
        var first = Lease("a");
        var second = Lease("b");
        await first.BootstrapAsync(TestContext.Current.CancellationToken);
        await second.BootstrapAsync(TestContext.Current.CancellationToken);

        Assert.True(first.HoldsLease);
        Assert.False(second.HoldsLease);
        Assert.Equal("a", await OwnerAsync());
    }

    [Fact(Skip = Skip, SkipUnless = nameof(LeaseTestsEnabled))]
    public async Task The_holder_hands_the_lease_to_the_other_slot()
    {
        await ResetAsync();
        var live = Lease("a");
        var standby = Lease("b");
        await live.BootstrapAsync(TestContext.Current.CancellationToken);
        await standby.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.False(standby.HoldsLease);

        // Promotion: the party that holds the lease writes the new owner (design 4.4).
        Assert.True(await live.TryAcquireAsync("b", TestContext.Current.CancellationToken));
        Assert.False(live.HoldsLease);
        Assert.Equal("b", await OwnerAsync());

        await standby.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.True(standby.HoldsLease);
        Assert.True(standby.Generation > 0);
    }

    [Fact(Skip = Skip, SkipUnless = nameof(LeaseTestsEnabled))]
    public async Task Releasing_gives_the_row_up_only_for_this_slot()
    {
        await ResetAsync();
        var live = Lease("a");
        var other = Lease("b");
        await live.BootstrapAsync(TestContext.Current.CancellationToken);

        // b does not hold it, so its release must leave a's ownership alone.
        await other.ReleaseAsync(TestContext.Current.CancellationToken);
        Assert.Equal("a", await OwnerAsync());

        await live.ReleaseAsync(TestContext.Current.CancellationToken);
        Assert.Equal(string.Empty, await OwnerAsync());
        Assert.False(live.HoldsLease);
    }

    [Fact(Skip = Skip, SkipUnless = nameof(LeaseTestsEnabled))]
    public async Task A_missing_table_reads_as_nobody_holding_the_lease()
    {
        await ResetAsync(create: false);
        var lease = Lease("a");
        await lease.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.False(lease.HoldsLease);
        Assert.Equal(0, lease.Generation);
    }

    private AzureTableActiveSlotLease Lease(string slot)
        => new(slot, _tables, TimeProvider.System, NullLogger<AzureTableActiveSlotLease>.Instance);

    private async Task<string?> OwnerAsync()
    {
        var row = await _tables.GetTableClient(ActiveSlotNames.Table)
            .GetEntityAsync<ActiveSlotLeaseEntity>(ActiveSlotNames.PartitionKey, ActiveSlotNames.RowKey,
                cancellationToken: TestContext.Current.CancellationToken);
        return row.Value.Owner;
    }

    private async Task ResetAsync(bool create = true)
    {
        await DropAsync(TestContext.Current.CancellationToken);
        if (create)
        {
            await _tables.CreateTableIfNotExistsAsync(ActiveSlotNames.Table, TestContext.Current.CancellationToken);
        }
    }

    private async Task DropAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _tables.DeleteTableAsync(ActiveSlotNames.Table, cancellationToken);
        }
        catch (RequestFailedException error) when (error.Status == 404)
        {
            // Nothing to drop.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (LeaseTestsEnabled)
        {
            await DropAsync(CancellationToken.None);
        }
    }
}
