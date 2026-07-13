using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace DigitalBrain.Tests.Spikes;

[GenerateSerializer]
[Alias("DigitalBrain.Tests.Spikes.SpikePayloadSynapse")]
public sealed record SpikePayloadSynapse(string Text) : Synapse(nameof(SpikePayloadSynapse), DateTimeOffset.UtcNow);

[Trait("Category", "cluster")]
[Collection(OrdinaryPersistenceClusterCollection.Name)]
public class OrdinaryPersistenceTests(OrdinaryPersistenceClusterFixture fixture)
{
    [Fact]
    public async Task Encrypted_ordinary_state_round_trips_a_polymorphic_synapse_after_reactivation()
    {
        var grain = fixture.Cluster.Client.GetGrain<IOrdinaryPersistenceProbeNeuron>("ordinary-persistence-" + Guid.NewGuid().ToString("N"));
        await grain.FireAsync(new SpikePayloadSynapse("spike-payload"));

        var activationInstanceId = await grain.GetActivationInstanceIdAsync();
        var timeline = await grain.GetTimelineAsync();
        Assert.Contains(timeline, static synapse => synapse is SpikePayloadSynapse { Text: "spike-payload" });

        await fixture.Cluster.DeactivateAsync((IAddressable)grain);

        var reactivatedInstanceId = await grain.GetActivationInstanceIdAsync();
        Assert.NotEqual(activationInstanceId, reactivatedInstanceId);
        var timelineAfterReactivation = await grain.GetTimelineAsync();
        Assert.Contains(timelineAfterReactivation, static synapse => synapse is SpikePayloadSynapse { Text: "spike-payload" });
    }

    [Fact]
    public async Task Encrypted_ordinary_state_retains_a_bounded_recent_timeline_across_reactivation()
    {
        var grain = fixture.Cluster.Client.GetGrain<IOrdinaryPersistenceProbeNeuron>("ordinary-retention-" + Guid.NewGuid().ToString("N"));

        for (var i = 0; i < 12; i++)
            await grain.FireAsync(new SpikePayloadSynapse($"payload-{i}"));

        Assert.Equal(
            Enumerable.Range(4, 8).Select(static i => $"payload-{i}"),
            (await grain.GetOutgoingTimelineAsync()).OfType<SpikePayloadSynapse>().Select(static synapse => synapse.Text));

        await fixture.Cluster.DeactivateAsync((IAddressable)grain);

        Assert.Equal(
            Enumerable.Range(4, 8).Select(static i => $"payload-{i}"),
            (await grain.GetOutgoingTimelineAsync()).OfType<SpikePayloadSynapse>().Select(static synapse => synapse.Text));
        await grain.FireAsync(new SpikePayloadSynapse("payload-after-reactivation"));
        Assert.Equal("payload-after-reactivation", Assert.IsType<SpikePayloadSynapse>((await grain.GetOutgoingTimelineAsync())[^1]).Text);
    }

    [Fact]
    public async Task Encrypted_ordinary_state_compacts_by_bytes_and_preserves_the_latest_synapse()
    {
        var grain = fixture.Cluster.Client.GetGrain<IOrdinaryPersistenceProbeNeuron>("ordinary-byte-retention-" + Guid.NewGuid().ToString("N"));

        for (var i = 0; i < 6; i++)
            await grain.FireAsync(new SpikePayloadSynapse($"payload-{i}:{new string('x', 2048)}"));

        var retained = (await grain.GetOutgoingTimelineAsync()).OfType<SpikePayloadSynapse>().ToArray();
        Assert.InRange(retained.Length, 1, 5);
        Assert.StartsWith("payload-5:", retained[^1].Text, StringComparison.Ordinal);

        await fixture.Cluster.DeactivateAsync((IAddressable)grain);

        var retainedAfterReactivation = (await grain.GetOutgoingTimelineAsync()).OfType<SpikePayloadSynapse>().ToArray();
        Assert.Equal(retained.Select(static synapse => synapse.Text), retainedAfterReactivation.Select(static synapse => synapse.Text));
    }

    [Fact]
    public async Task Rejected_oversize_synapse_does_not_block_subsequent_persistence()
    {
        var grain = fixture.Cluster.Client.GetGrain<IOrdinaryPersistenceProbeNeuron>("ordinary-oversize-" + Guid.NewGuid().ToString("N"));
        await grain.FireAsync(new SpikePayloadSynapse("before-oversize"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            grain.FireAsync(new SpikePayloadSynapse(new string('x', 16 * 1024))));

        await grain.FireAsync(new SpikePayloadSynapse("after-oversize"));
        Assert.Equal(
            ["before-oversize", "after-oversize"],
            (await grain.GetOutgoingTimelineAsync()).OfType<SpikePayloadSynapse>().Select(static synapse => synapse.Text));
    }
}

[Alias("DigitalBrain.Tests.Spikes.IOrdinaryPersistenceProbeNeuron")]
public interface IOrdinaryPersistenceProbeNeuron : INeuron
{
    [Alias("GetActivationInstanceIdAsync")]
    Task<string> GetActivationInstanceIdAsync();
}

[GrainType("digitalbrain.test.ordinary-persistence-probe")]
public sealed class OrdinaryPersistenceProbeNeuron(
    ILogger<OrdinaryPersistenceProbeNeuron> logger,
    [PersistentState("timeline", "Default")]
    IPersistentState<DigitalBrain.Kernel.Runtime.EncryptedRuntimeStateEnvelope> persistentState,
    EncryptedRuntimeStateProtector protector)
    : Neuron(logger, persistentState, protector), IOrdinaryPersistenceProbeNeuron, IHandle<SpikePayloadSynapse>
{
    private string _activationInstanceId = string.Empty;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _activationInstanceId = Guid.NewGuid().ToString("N");
        await base.OnActivateAsync(cancellationToken);
    }

    public Task HandleAsync(SpikePayloadSynapse synapse, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<string> GetActivationInstanceIdAsync() => Task.FromResult(_activationInstanceId);
}
