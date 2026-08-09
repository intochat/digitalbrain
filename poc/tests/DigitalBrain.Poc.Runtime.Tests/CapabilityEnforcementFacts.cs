using DigitalBrain.Poc.Abstractions;
using DigitalBrain.Poc.Runtime;
using Xunit;

namespace DigitalBrain.Poc.Runtime.Tests;

public sealed class CapabilityEnforcementFacts
{
    [Fact]
    public async Task InvocationCannotFireAnUngrantedContract()
    {
        var brain = new BrainFacade(_ => Task.CompletedTask);
        var invocation = brain.ForCandidate(
            CandidateInvocationScope.ForTest(
                "owner-a",
                CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa"),
                "revision-1"),
            [typeof(AllowedOutput)]);

        await Assert.ThrowsAsync<CapabilityDeniedException>(
            () => invocation.FireSynapse(
                new ForbiddenOutput(),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CandidateLocalEnvelopePinsProducingAndTargetRevision()
    {
        SynapseEnvelope? captured = null;
        var brain = new BrainFacade(envelope =>
        {
            captured = envelope;
            return Task.CompletedTask;
        });
        var scope = CandidateInvocationScope.ForTest(
            "owner-a",
            CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa"),
            "revision-7");

        await brain.ForCandidate(scope, [typeof(AllowedOutput)])
            .FireSynapse(new AllowedOutput(), TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("revision-7", captured.ProducingRevision);
        Assert.Equal("revision-7", captured.TargetRevision);
        Assert.Equal("owner-a", captured.OwnerId);
    }

    private sealed record AllowedOutput : Synapse;

    private sealed record ForbiddenOutput : Synapse;
}
