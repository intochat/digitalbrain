using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Time.Tests;

public sealed class ClientEntryPointCapability(TimeFixture fixture)
{
    [Fact(DisplayName =
        "ClientEntryPoint ICountdown.Start from an unattributed client does not journal capability facts")]
    public async Task ClientEntryPointStartDoesNotJournalCapabilityFacts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var (countdown, destination) = TimeFixture.Pair(test);

        var started = await TimeFixture.Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        Assert.Equal(CountdownStatus.Scheduled, started.Status);

        Assert.Empty(await countdown.Outgoing.ReadAsync<CapabilityRequested>(
            cancellationToken: cancellationToken));
        Assert.Empty(await countdown.Outgoing.ReadAsync<CapabilityCompleted>(
            cancellationToken: cancellationToken));
        Assert.Empty(await countdown.Outgoing.ReadAsync<CapabilityFailed>(
            cancellationToken: cancellationToken));
        Assert.Empty(await countdown.Outgoing.ReadAsync<CapabilityRejected>(
            cancellationToken: cancellationToken));
        Assert.Empty(await countdown.Incoming.ReadAsync<CapabilityRequested>(
            cancellationToken: cancellationToken));
        Assert.Empty(await countdown.Incoming.ReadAsync<CapabilityCompleted>(
            cancellationToken: cancellationToken));
    }
}
