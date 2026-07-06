using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace DigitalBrain.Tests.Spikes;

#pragma warning disable ORLEANSEXP005

[Trait("Category", "cluster")]
[Collection(OrleansJournalClusterCollection.Name)]
public class JournalFormatSpikeTests(OrleansJournalClusterFixture fixture)
{
    [Fact]
    public async Task Orleans_Json_Format_Round_Trips_A_Synapse_With_JournalJsonResolver()
    {
        var grain = fixture.Cluster.Client.GetGrain<IJournalFormatProbeNeuron>("spike-json-format-" + Guid.NewGuid().ToString("N"));
        await grain.FireAsync(new DemoMessageSynapse("spike-payload"));

        var activationInstanceId = await grain.GetActivationInstanceIdAsync();
        var timeline = await grain.GetTimelineAsync();
        Assert.Contains(timeline, s => s is DemoMessageSynapse d && d.Text == "spike-payload");

        // Write-only round-trips prove serialization works, but not deserialization. DeactivateAsync
        // waits for deactivation; the next call creates a fresh activation and rebuilds from journal bytes.
        await fixture.Cluster.DeactivateAsync((IAddressable)grain);

        var reactivatedInstanceId = await grain.GetActivationInstanceIdAsync();
        Assert.NotEqual(activationInstanceId, reactivatedInstanceId);

        var timelineAfterReactivation = await grain.GetTimelineAsync();
        Assert.Contains(timelineAfterReactivation, s => s is DemoMessageSynapse d && d.Text == "spike-payload");
    }
}

public interface IJournalFormatProbeNeuron : INeuron
{
    Task<string> GetActivationInstanceIdAsync();
}

[GrainType("digitalbrain.test.journal-format-probe")]
public sealed class JournalFormatProbeNeuron(ILogger<JournalFormatProbeNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IJournalFormatProbeNeuron, IHandle<DemoMessageSynapse>
{
    private string _activationInstanceId = string.Empty;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        _activationInstanceId = Guid.NewGuid().ToString("N");
        await base.OnActivateAsync(ct);
    }

    public Task HandleAsync(DemoMessageSynapse synapse) => Task.CompletedTask;

    public Task<string> GetActivationInstanceIdAsync() => Task.FromResult(_activationInstanceId);
}
