using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

[CollectionDefinition("HostedExclusive", DisableParallelization = true)]
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "xUnit requires collection definition types to be public.")]
public sealed class HostedExclusiveCollectionDefinition;

[Collection("HostedExclusive")]
public sealed class HostedApplicationContracts
{
    [Fact(DisplayName = "second concurrent HostedApplication exclusive hold waits until the first lease is released")]
    public async Task SecondConcurrentExclusiveHoldWaitsUntilFirstReleased()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.False(HostedApplication.IsExclusiveHeld);

        await using var first = await HostedApplication.HoldExclusiveAsync("contract-first", cancellationToken);

        Assert.True(HostedApplication.IsExclusiveHeld);
        Assert.Equal("contract-first", HostedApplication.ExclusiveOwner);

        var secondOpen = HostedApplication.HoldExclusiveAsync("contract-second", cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);

        Assert.False(secondOpen.IsCompleted);
        Assert.True(HostedApplication.IsExclusiveHeld);
        Assert.Equal("contract-first", HostedApplication.ExclusiveOwner);

        await first.DisposeAsync();

        await using var second = await secondOpen.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        Assert.True(HostedApplication.IsExclusiveHeld);
        Assert.Equal("contract-second", HostedApplication.ExclusiveOwner);
    }

    [Fact(DisplayName = "releasing the exclusive HostedApplication lease clears IsExclusiveHeld")]
    public async Task ReleasingExclusiveLeaseClearsHeldFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var lease = await HostedApplication.HoldExclusiveAsync("contract-release", cancellationToken);

        Assert.True(HostedApplication.IsExclusiveHeld);
        await lease.DisposeAsync();

        Assert.False(HostedApplication.IsExclusiveHeld);
        Assert.Null(HostedApplication.ExclusiveOwner);
    }
}
