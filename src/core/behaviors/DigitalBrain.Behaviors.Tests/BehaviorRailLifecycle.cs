using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorRailLifecycle(BehaviorsFixture fixture)
{
    [Fact(DisplayName = "propose → compile success journals artifact hash; compile failure is a typed journaled fact")]
    public async Task ProposeJournalsCompileSuccessOrTypedFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var behavior = test.Neuron<IBehaviorNeuron>(BehaviorsFixture.SampleBehavior);

        var successCommand = CommandId.New();
        var proposedWait = behavior.Outgoing.NextAsync<BehaviorRevisionProposed>(cancellationToken);
        var compileOkWait = behavior.Outgoing.NextAsync<BehaviorCompileSucceeded>(cancellationToken);

        var proposed = await behavior.Reference.Propose(new ProposeBehaviorRevision(
            successCommand,
            RailPrograms.GreenProgram(),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["install"] = RailPrograms.GreenFeature },
            "Sample",
            "Sample behavior"));

        Assert.Equal(BehaviorRevisionStatus.Proposed, proposed.Status);
        Assert.False(string.IsNullOrWhiteSpace(proposed.ProposedArtifactHash));
        Assert.Null(proposed.ActiveArtifactHash);

        var proposedFact = await proposedWait;
        var compileOk = await compileOkWait;
        Assert.Equal(proposed.ProposedArtifactHash, proposedFact.Synapse.ArtifactHash);
        Assert.Equal(proposed.ProposedArtifactHash, compileOk.Synapse.ArtifactHash);

        var failCommand = CommandId.New();
        var compileFailWait = behavior.Outgoing.NextAsync<BehaviorCompileFailed>(cancellationToken);
        var failed = await behavior.Reference.Propose(new ProposeBehaviorRevision(
            failCommand,
            RailPrograms.BrokenProgram(),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["install"] = RailPrograms.GreenFeature },
            "Sample",
            "Sample behavior"));

        Assert.Equal(BehaviorRevisionStatus.CompileFailed, failed.Status);
        Assert.False(string.IsNullOrWhiteSpace(failed.LastCompileFailure));
        var compileFailed = await compileFailWait;
        Assert.Contains("error", compileFailed.Synapse.Diagnostics, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "BDD gate red blocks approval; green unblocks approval")]
    public async Task BddGateRedBlocksApprovalGreenUnblocks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var behavior = test.Neuron<IBehaviorNeuron>(BehaviorsFixture.SampleBehavior);

        var red = await behavior.Reference.Propose(new ProposeBehaviorRevision(
            CommandId.New(),
            RailPrograms.RedProgram(),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["install"] = RailPrograms.RedFeature },
            "Sample",
            "Sample behavior"));
        var redTests = await behavior.Reference.RunTests(new RunBehaviorTests(CommandId.New(), red.ProposedArtifactHash!));
        Assert.Equal(BehaviorRevisionStatus.TestsFailed, redTests.Status);
        Assert.False(redTests.TestsPassed);

        var redApproval = Approval(test, CommandId.New(), red.ProposedArtifactHash!);
        await DeliverApprovalAsync(test, behavior, redApproval, cancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => behavior.Reference.Approve(redApproval));

        var green = await behavior.Reference.Propose(new ProposeBehaviorRevision(
            CommandId.New(),
            RailPrograms.GreenProgram(),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["install"] = RailPrograms.GreenFeature },
            "Sample",
            "Sample behavior"));
        var greenTests = await behavior.Reference.RunTests(new RunBehaviorTests(CommandId.New(), green.ProposedArtifactHash!));
        Assert.Equal(BehaviorRevisionStatus.TestsPassed, greenTests.Status);
        Assert.True(greenTests.TestsPassed);

        var greenApproval = Approval(test, CommandId.New(), green.ProposedArtifactHash!);
        await DeliverApprovalAsync(test, behavior, greenApproval, cancellationToken);
        var approved = await behavior.Reference.Approve(greenApproval);
        Assert.True(approved.IsApproved);
        Assert.Equal(BehaviorRevisionStatus.Approved, approved.Status);
    }

    [Fact(DisplayName = "approval evidence binds to artifact hash; stale or mismatched hash is refused and journaled")]
    public async Task ApprovalEvidenceBindsToArtifactHash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var behavior = test.Neuron<IBehaviorNeuron>(BehaviorsFixture.SampleBehavior);

        var first = await ProposeGreenAsync(behavior);
        await behavior.Reference.RunTests(new RunBehaviorTests(CommandId.New(), first.ProposedArtifactHash!));

        var second = await ProposeGreenAsync(behavior, outcome: "v2");
        await behavior.Reference.RunTests(new RunBehaviorTests(CommandId.New(), second.ProposedArtifactHash!));

        var stale = Approval(test, CommandId.New(), first.ProposedArtifactHash!);
        await DeliverApprovalAsync(test, behavior, stale, cancellationToken);
        var refusedWait = behavior.Outgoing.NextAsync<BehaviorRevisionApprovalRefused>(cancellationToken);
        await Assert.ThrowsAsync<NeuronAuthorizationException>(() => behavior.Reference.Approve(stale));
        var refused = await refusedWait;
        Assert.Equal(first.ProposedArtifactHash, refused.Synapse.AttemptedFingerprint);
        Assert.Equal("stale-or-mismatched-artifact-hash", refused.Synapse.Reason);

        var current = Approval(test, CommandId.New(), second.ProposedArtifactHash!);
        await DeliverApprovalAsync(test, behavior, current, cancellationToken);
        var approved = await behavior.Reference.Approve(current);
        Assert.True(approved.IsApproved);
        Assert.Equal(second.ProposedArtifactHash, approved.ProposedArtifactHash);
    }

    [Fact(DisplayName = "activate → execute via test executor → result journaled; rollback restores prior revision for next execution")]
    public async Task ActivateExecuteAndRollbackRestorePriorRevision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var behavior = test.Neuron<IBehaviorNeuron>(BehaviorsFixture.SampleBehavior);

        var first = await InstallAsync(test, behavior, RailPrograms.GreenProgram("v1"), "v1");
        var firstHash = first.ActiveArtifactHash!;

        var second = await InstallAsync(test, behavior, RailPrograms.GreenProgram("v2"), "v2");
        Assert.Equal(firstHash, second.PriorArtifactHash);
        Assert.NotEqual(firstHash, second.ActiveArtifactHash);

        var executeWait = behavior.Outgoing.NextAsync<BehaviorExecuted>(cancellationToken);
        var executed = await behavior.Reference.Execute(new ExecuteBehaviorRevision(
            CommandId.New(),
            "SampleTrigger",
            """{"Label":"run"}"""));
        Assert.True(executed.Succeeded, executed.Outcome);
        Assert.Equal("v2:run", executed.Outcome);
        Assert.Equal(second.ActiveArtifactHash, (await executeWait).Synapse.ArtifactHash);

        var rolled = await behavior.Reference.Rollback(new RollbackBehaviorRevision(CommandId.New()));
        Assert.Equal(firstHash, rolled.ActiveArtifactHash);
        Assert.Equal(second.ActiveArtifactHash, rolled.PriorArtifactHash);

        var rollbackExecute = await behavior.Reference.Execute(new ExecuteBehaviorRevision(
            CommandId.New(),
            "SampleTrigger",
            """{"Label":"after-rollback"}"""));
        Assert.True(rollbackExecute.Succeeded, rollbackExecute.Outcome);
        Assert.Equal("v1:after-rollback", rollbackExecute.Outcome);
        Assert.Equal(firstHash, rollbackExecute.ArtifactHash);
    }

    private static async Task<BehaviorSnapshot> ProposeGreenAsync(
        TestNeuron<IBehaviorNeuron> behavior,
        string outcome = "v1-green")
    {
        return await behavior.Reference.Propose(new ProposeBehaviorRevision(
            CommandId.New(),
            RailPrograms.GreenProgram(outcome),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["install"] = RailPrograms.GreenFeature },
            "Sample",
            "Sample behavior"));
    }

    private static async Task<BehaviorSnapshot> InstallAsync(
        TestBrain test,
        TestNeuron<IBehaviorNeuron> behavior,
        string program,
        string label)
    {
        var proposed = await behavior.Reference.Propose(new ProposeBehaviorRevision(
            CommandId.New(),
            program,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["install"] = RailPrograms.GreenFeature },
            $"Sample {label}",
            $"Sample behavior {label}"));
        await behavior.Reference.RunTests(new RunBehaviorTests(CommandId.New(), proposed.ProposedArtifactHash!));
        var approval = Approval(test, CommandId.New(), proposed.ProposedArtifactHash!);
        await DeliverApprovalAsync(test, behavior, approval, TestContext.Current.CancellationToken);
        await behavior.Reference.Approve(approval);
        return await behavior.Reference.Activate(new ActivateBehaviorRevision(CommandId.New(), proposed.ProposedArtifactHash!));
    }

    private static BehaviorRevisionApproval Approval(TestBrain test, CommandId commandId, string fingerprint)
        => new(
            Guid.NewGuid(),
            commandId,
            fingerprint,
            ISessionNeuron.ForOwner(test.Client.Owner),
            test.Clock.UtcNow);

    private static async Task DeliverApprovalAsync(
        TestBrain test,
        TestNeuron<IBehaviorNeuron> behavior,
        BehaviorRevisionApproval approval,
        CancellationToken cancellationToken)
    {
        var wait = behavior.Incoming.NextAsync<BehaviorRevisionApproval>(cancellationToken);
        await test.Client.SendAsync(behavior.Id, approval);
        _ = await wait;
    }
}
