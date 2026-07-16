using Brain.Contracts;
using Xunit;

namespace Brain.KernelTests;

public class EffectGateTests(ClusterFixture fixture) : IClassFixture<ClusterFixture>
{
    [Fact]
    public async Task Proposing_kind_gets_effect_key_and_effect_awaits_decision()
    {
        var neuron = fixture.Neuron("proposer", Guid.NewGuid().ToString("N"));
        var receipt = await neuron.InvokeAsync(new("proposer.send.v1", """{"to":"x"}""", "cmd-1", fixture.OwnerSession));
        Assert.NotNull(receipt.EffectKey);
        var effect = fixture.Cluster.GrainFactory.GetGrain<INeuron>(receipt.EffectKey!);
        var claim = await Assert.ThrowsAsync<BrainException>(() =>
            effect.InvokeAsync(new("effect.claim-proof.v1", "{}", "cmd-2", fixture.OwnerSession)));
        Assert.Equal(BrainErrors.EffectNotApproved, claim.Code);
    }

    [Fact]
    public async Task Approved_effect_yields_proof_exactly_once()
    {
        var neuron = fixture.Neuron("proposer", Guid.NewGuid().ToString("N"));
        var receipt = await neuron.InvokeAsync(new("proposer.send.v1", """{"to":"x"}""", "cmd-1", fixture.OwnerSession));
        var effect = fixture.Cluster.GrainFactory.GetGrain<INeuron>(receipt.EffectKey!);
        await effect.InvokeAsync(new("effect.approve.v1", "{}", "cmd-approve", fixture.OwnerSession));
        var proof = await effect.InvokeAsync(new("effect.claim-proof.v1", "{}", "cmd-claim", fixture.OwnerSession));
        Assert.Contains("payloadDigest", proof.OutputJson);
        var replay = await effect.InvokeAsync(new("effect.claim-proof.v1", "{}", "cmd-claim", fixture.OwnerSession));
        Assert.Equal(proof, replay);
        var secondClaim = await Assert.ThrowsAsync<BrainException>(() =>
            effect.InvokeAsync(new("effect.claim-proof.v1", "{}", "cmd-claim-2", fixture.OwnerSession)));
        Assert.Equal(BrainErrors.EffectNotApproved, secondClaim.Code);
    }
}
