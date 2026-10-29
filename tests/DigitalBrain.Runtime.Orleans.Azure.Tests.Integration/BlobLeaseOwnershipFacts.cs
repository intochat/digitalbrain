using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Runtime.Orleans.Azure.Tests.Integration;

// T-03-40: two writers on one brain, against real blob leases on Azurite.
public sealed class BlobLeaseOwnershipFacts(AzuriteFixture azurite)
{
    private async Task<BlobContainerClient> Container(CancellationToken ct)
    {
        var container = new BlobServiceClient(azurite.Require()).GetBlobContainerClient("leases-" + Guid.NewGuid().ToString("N")[..12]);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        return container;
    }

    private static BlobLeaseBrainLogOwnership Owner(BlobContainerClient container) => new(container, TimeProvider.System,
        NullLoggerFactory.Instance, TimeSpan.FromSeconds(2), BlobLeaseBrainLogOwnership.DefaultWriteTimeout);

    [Fact]
    public async Task Of_two_owners_only_one_holds_the_lease_until_it_releases()
    {
        var ct = TestContext.Current.CancellationToken;
        var container = await Container(ct);
        var first = Owner(container);
        var second = Owner(container);

        var lease = await first.ClaimAsync("unused", "brain-1", ct);
        Assert.True(lease.IsHeld);
        var refused = await Assert.ThrowsAsync<BrainNotOwnedException>(() => second.ClaimAsync("unused", "brain-1", ct));
        Assert.Equal("brain-1", refused.BrainId);
        Assert.Equal(1, second.Snapshot().Refused);

        var mark = lease.BeginWrite();
        lease.EndWrite(mark);
        await lease.DisposeAsync();
        Assert.Equal(1, first.Snapshot().Released);

        await using var taken = await second.ClaimAsync("unused", "brain-1", ct);
        Assert.True(taken.IsHeld);
        Assert.Equal("blob-lease", second.Snapshot().Mechanism);
    }

    [Fact]
    public async Task Different_brains_are_owned_independently()
    {
        var ct = TestContext.Current.CancellationToken;
        var container = await Container(ct);
        await using var a = await Owner(container).ClaimAsync("unused", "brain-a", ct);
        await using var b = await Owner(container).ClaimAsync("unused", "brain-b", ct);
        Assert.True(a.IsHeld && b.IsHeld);
    }
}
