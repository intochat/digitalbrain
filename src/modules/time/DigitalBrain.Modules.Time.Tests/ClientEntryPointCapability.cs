using DigitalBrain.Abstractions;
using Xunit;

namespace DigitalBrain.Time.Tests;

public sealed class ClientEntryPointCapability : CountdownTest
{

    [Fact(DisplayName =
        "ClientEntryPoint ICountdown.Start from an unattributed client does not journal capability facts")]
    public async Task ClientEntryPointStartDoesNotJournalCapabilityFacts()
    {
        var cancellationToken = Cancellation;
        var (countdown, destination) = await PairAsync();

        var started = await StartAsync(countdown, destination, TimeSpan.FromHours(1));

        Assert.Equal(CountdownStatus.Scheduled, started.Status);

        Assert.Empty(await countdown.Outgoing.ReadAsync<CapabilityRequested>(cancellationToken: cancellationToken));
        Assert.Empty(await countdown.Outgoing.ReadAsync<CapabilityCompleted>(cancellationToken: cancellationToken));
        Assert.Empty(await countdown.Outgoing.ReadAsync<CapabilityFailed>(cancellationToken: cancellationToken));
        Assert.Empty(await countdown.Outgoing.ReadAsync<CapabilityRejected>(cancellationToken: cancellationToken));
        Assert.Empty(await countdown.Incoming.ReadAsync<CapabilityRequested>(cancellationToken: cancellationToken));
        Assert.Empty(await countdown.Incoming.ReadAsync<CapabilityCompleted>(cancellationToken: cancellationToken));
    }
}
