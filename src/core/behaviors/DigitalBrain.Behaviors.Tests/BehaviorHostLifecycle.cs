using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Security;
using DigitalBrain.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Behaviors.Host;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorHostLifecycle(HostBehaviorsFixture fixture)
{
    [Fact(DisplayName = "signed deploy loads and executes; unsigned and tampered artifacts are refused and journaled")]
    public async Task SignedLoadHappyPathAndRefuseTamperedOrUnsigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        BehaviorHostTestFaults.Reset();
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var behavior = test.Neuron<IBehaviorNeuron>(BehaviorsFixture.SampleBehavior);

        var proposed = await ProposeAndTestAsync(behavior, RailPrograms.GreenProgram("host-ok"));
        var approval = Approval(test, CommandId.New(), proposed.ProposedArtifactHash!);
        await DeliverApprovalAsync(test, behavior, approval, cancellationToken);
        await behavior.Reference.Approve(approval);

        var deployedWait = behavior.Outgoing.NextAsync<BehaviorRevisionDeployed>(cancellationToken);
        var activated = await behavior.Reference.Activate(
            new ActivateBehaviorRevision(CommandId.New(), proposed.ProposedArtifactHash!));
        Assert.Equal(BehaviorRevisionStatus.Active, activated.Status);
        Assert.Equal(proposed.ProposedArtifactHash, (await deployedWait).Synapse.ArtifactHash);

        var executed = await behavior.Reference.Execute(new ExecuteBehaviorRevision(
            CommandId.New(),
            "SampleTrigger",
            """{"Label":"signed"}"""));
        Assert.True(executed.Succeeded, executed.Outcome);
        Assert.Equal(BehaviorExecutionCodes.Succeeded, executed.Outcome);

        var unsigned = await ProposeAndTestAsync(behavior, RailPrograms.GreenProgram("unsigned-path"));
        var unsignedApproval = Approval(test, CommandId.New(), unsigned.ProposedArtifactHash!);
        await DeliverApprovalAsync(test, behavior, unsignedApproval, cancellationToken);
        await behavior.Reference.Approve(unsignedApproval);

        BehaviorHostTestFaults.RefuseNextDeploy("unsigned-artifact");
        var unsignedRefuse = behavior.Outgoing.NextAsync<BehaviorRevisionDeployRefused>(cancellationToken);
        var unsignedFailure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Reference.Activate(
                new ActivateBehaviorRevision(CommandId.New(), unsigned.ProposedArtifactHash!)));
        Assert.Contains("unsigned-artifact", unsignedFailure.Message, StringComparison.Ordinal);
        Assert.Equal("unsigned-artifact", (await unsignedRefuse).Synapse.Reason);

        var tampered = await ProposeAndTestAsync(behavior, RailPrograms.GreenProgram("tamper-path"));
        var tamperedApproval = Approval(test, CommandId.New(), tampered.ProposedArtifactHash!);
        await DeliverApprovalAsync(test, behavior, tamperedApproval, cancellationToken);
        await behavior.Reference.Approve(tamperedApproval);

        BehaviorHostTestFaults.RefuseNextDeploy("invalid-signature");
        var tamperRefuse = behavior.Outgoing.NextAsync<BehaviorRevisionDeployRefused>(cancellationToken);
        var tamperedFailure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Reference.Activate(
                new ActivateBehaviorRevision(CommandId.New(), tampered.ProposedArtifactHash!)));
        Assert.Contains("invalid-signature", tamperedFailure.Message, StringComparison.Ordinal);
        Assert.Equal("invalid-signature", (await tamperRefuse).Synapse.Reason);

        AssertTrustVerifiesSignatures();
    }

    [Fact(DisplayName = "deploy → activate → execute → rollback through host seam with scripted broker")]
    public async Task DeployActivateExecuteRollbackThroughHostSeam()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        BehaviorHostTestFaults.Reset();
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var behavior = test.Neuron<IBehaviorNeuron>(BehaviorsFixture.SampleBehavior);

        var first = await InstallOnHostAsync(test, behavior, RailPrograms.GreenProgram("v1"), "v1");
        var firstHash = first.ActiveArtifactHash!;

        var second = await InstallOnHostAsync(test, behavior, RailPrograms.GreenProgram("v2"), "v2");
        Assert.Equal(firstHash, second.PriorArtifactHash);

        var executed = await behavior.Reference.Execute(new ExecuteBehaviorRevision(
            CommandId.New(),
            "SampleTrigger",
            """{"Label":"run"}"""));
        Assert.True(executed.Succeeded, executed.Outcome);
        Assert.Equal(BehaviorExecutionCodes.Succeeded, executed.Outcome);

        var rolled = await behavior.Reference.Rollback(new RollbackBehaviorRevision(CommandId.New()));
        Assert.Equal(firstHash, rolled.ActiveArtifactHash);

        var afterRollback = await behavior.Reference.Execute(new ExecuteBehaviorRevision(
            CommandId.New(),
            "SampleTrigger",
            """{"Label":"restored"}"""));
        Assert.True(afterRollback.Succeeded, afterRollback.Outcome);
        Assert.Equal(BehaviorExecutionCodes.Succeeded, afterRollback.Outcome);
        Assert.Equal(firstHash, afterRollback.ArtifactHash);
    }

    private static void AssertTrustVerifiesSignatures()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Security:StateProtectionKey"] = Convert.ToBase64String(new byte[32]),
            })
            .Build();
        DurablePayloadProtectionHosting.Configure(services, configuration);
        services.AddSingleton<IBehaviorArtifactTrust>(static provider =>
            new BehaviorArtifactTrust(provider.GetRequiredService<IDurablePayloadProtector>()));
        using var provider = services.BuildServiceProvider();
        var trust = provider.GetRequiredService<IBehaviorArtifactTrust>();

        var hash = BehaviorArtifactDigest.Compute("artifact"u8).Value;
        var signature = trust.Sign(hash);
        trust.Verify(hash, signature);

        Assert.Throws<BehaviorHostException>(() => trust.Verify(hash, ReadOnlySpan<byte>.Empty));
        var tampered = signature.ToArray();
        tampered[0] ^= 0xFF;
        Assert.Throws<BehaviorHostException>(() => trust.Verify(hash, tampered));
    }

    private static async Task<BehaviorSnapshot> ProposeAndTestAsync(
        TestNeuron<IBehaviorNeuron> behavior,
        string program)
    {
        var proposed = await behavior.Reference.Propose(new ProposeBehaviorRevision(
            CommandId.New(),
            program,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["install"] = RailPrograms.GreenFeature },
            "Sample",
            "Sample behavior"));
        return await behavior.Reference.RunTests(new RunBehaviorTests(CommandId.New(), proposed.ProposedArtifactHash!));
    }

    private static async Task<BehaviorSnapshot> InstallOnHostAsync(
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
        return await behavior.Reference.Activate(
            new ActivateBehaviorRevision(CommandId.New(), proposed.ProposedArtifactHash!));
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
        await test.Client.SendAsync(behavior.Id, approval, cancellationToken);
        _ = await wait;
    }
}

public sealed class HostBehaviorsFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        BehaviorHostTestFaults.Reset();
        brain.AddModule<BehaviorsModule>();
        brain.AddModule<InProcessBehaviorHostGatewayModule>();
        brain.Configure(BehaviorsModule.ExecutorConfigurationKey, BehaviorsModule.HostExecutorName);
    }
}
