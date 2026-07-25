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
        var countdown = test.Neuron<ICountdown>("entry");
        var destination = test.Neuron<ICountdown>("destination");

        var started = await countdown.Reference.Start(new StartCountdown(
            CommandId.New(),
            TimeSpan.FromHours(1),
            destination.Id));

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
