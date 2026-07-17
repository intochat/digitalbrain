using System.Text.Json;
using Brain.Contracts;
using Brain.Modules.Sdk;
using Xunit;

namespace Brain.KernelTests;

public class ProductFoundationBaselineTests(BrainClusterFixture<KernelKindsConfigurator> fixture)
    : BrainTest<KernelKindsConfigurator>(fixture)
{
    [Fact]
    public async Task Repeating_a_command_returns_the_original_receipt_without_a_second_event()
    {
        var neuron = Neuron("test", Guid.NewGuid().ToString("N"));
        var commandId = CommandId();
        var first = await neuron.InvokeAsync(Echo(commandId, """{"value":1}"""));

        var replay = await neuron.InvokeAsync(Echo(commandId, """{"value":2}"""));

        Assert.Equal(first, replay);
        var events = await neuron.ReadEventsAsync(0, 10);
        var recorded = Assert.Single(events.Events);
        Assert.Equal(commandId, recorded.CommandId);
        Assert.Equal("""{"value":1}""", recorded.PayloadJson);
    }

    [Fact]
    public async Task Expected_revision_rejects_stale_writes()
    {
        var neuron = Neuron("test", Guid.NewGuid().ToString("N"));
        await neuron.InvokeAsync(Echo(CommandId(), "{}"));
        var stale = Echo(CommandId(), "{}") with { ExpectedRevision = 0 };

        var exception = await Assert.ThrowsAsync<BrainException>(() => neuron.InvokeAsync(stale));

        Assert.Equal(BrainErrors.RevisionConflict, exception.Code);
        Assert.Single((await neuron.ReadEventsAsync(0, 10)).Events);
    }

    [Fact]
    public async Task Grant_is_bound_to_owner_space_grantee_and_allowed_contract()
    {
        var neuron = Neuron("test", Guid.NewGuid().ToString("N"));
        const string grantee = "partner-owner|actor/allowed|session/grantee";
        var grant = JsonSerializer.Serialize(new { granteeKey = grantee, contract = "test.echo.v1" });
        await neuron.InvokeAsync(new("neuron.grant.v1", grant, CommandId(), OwnerSession));

        var accepted = await neuron.InvokeAsync(new("test.echo.v1", "{}", CommandId(), grantee));

        Assert.Equal("accepted", accepted.Status);
        await AssertGrantMissing(neuron, "other-owner|actor/allowed|session/grantee", "test.echo.v1");
        await AssertGrantMissing(neuron, "partner-owner|actor/other|session/grantee", "test.echo.v1");
        await AssertGrantMissing(neuron, "partner-owner|actor/allowed|session/other", "test.echo.v1");
        await AssertGrantMissing(neuron, grantee, "test.other.v1");
        await AssertGrantMissing(Neuron("test", Guid.NewGuid().ToString("N")), grantee, "test.echo.v1");
    }

    [Fact]
    public async Task Effect_cannot_be_claimed_before_approval()
    {
        var proposer = Neuron("proposer", Guid.NewGuid().ToString("N"));
        var proposal = await proposer.InvokeAsync(new(
            "proposer.send.v1",
            """{"to":"recipient@example.com"}""",
            CommandId(),
            OwnerSession));
        var effect = Cluster.GrainFactory.GetGrain<INeuron>(proposal.EffectKey!);

        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            effect.InvokeAsync(new("effect.claim-proof.v1", "{}", CommandId(), OwnerSession)));

        Assert.Equal(BrainErrors.EffectNotApproved, exception.Code);
        Assert.Single((await effect.ReadEventsAsync(0, 10)).Events);
    }

    [Fact]
    public async Task Approved_effect_is_claimed_exactly_once()
    {
        var proposer = Neuron("proposer", Guid.NewGuid().ToString("N"));
        var proposal = await proposer.InvokeAsync(new(
            "proposer.send.v1",
            """{"to":"recipient@example.com"}""",
            CommandId(),
            OwnerSession));
        var effect = Cluster.GrainFactory.GetGrain<INeuron>(proposal.EffectKey!);
        await effect.InvokeAsync(new("effect.approve.v1", "{}", CommandId(), OwnerSession));
        var claimCommandId = CommandId();

        var claim = await effect.InvokeAsync(new("effect.claim-proof.v1", "{}", claimCommandId, OwnerSession));
        var replay = await effect.InvokeAsync(new("effect.claim-proof.v1", "{}", claimCommandId, OwnerSession));
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            effect.InvokeAsync(new("effect.claim-proof.v1", "{}", CommandId(), OwnerSession)));

        Assert.Equal(claim, replay);
        Assert.Equal(BrainErrors.EffectNotApproved, exception.Code);
        Assert.Equal(["proposed", "approved", "claimed"],
            (await effect.ReadEventsAsync(0, 10)).Events.Select(entry => entry.Kind));
    }

    private static NeuronInvocation Echo(string commandId, string input) =>
        new("test.echo.v1", input, commandId, "owner|actor/test|session/t");

    private static string CommandId() => Guid.NewGuid().ToString("N");

    private static async Task AssertGrantMissing(INeuron neuron, string caller, string contract)
    {
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            neuron.InvokeAsync(new(contract, "{}", CommandId(), caller)));
        Assert.Equal(BrainErrors.GrantMissing, exception.Code);
    }
}
