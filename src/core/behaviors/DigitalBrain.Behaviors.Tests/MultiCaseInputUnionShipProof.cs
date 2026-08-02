using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class MultiCaseInputUnionShipProof(BehaviorsFixture fixture)
{
    private static readonly string[] ExpectedCaseIds =
    [
        "case.GmailMessageReceived",
        "case.ManualResearchRequest",
    ];

    [Fact(DisplayName =
        "Multi-case root union publishes and binds by stable case ids without central interface edit")]
    public async Task MultiCaseRootUnionPublishesAndBindsByStableCaseIds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var program = RailPrograms.UnionGreenProgram();

        Assert.Contains("public sealed record ManualResearchRequest", program, StringComparison.Ordinal);
        Assert.Contains("public sealed record GmailMessageReceived", program, StringComparison.Ordinal);
        Assert.Contains("public union ResearchCompanyRequest(ManualResearchRequest, GmailMessageReceived)", program, StringComparison.Ordinal);

        var compile = new BehaviorCompiler().Compile(
            program,
            new BehaviorId(BehaviorsFixture.SampleBehavior));
        Assert.True(compile.Succeeded, compile.Diagnostics);
        Assert.NotNull(compile.Contract);
        Assert.Equal(BehaviorsFixture.SampleBehavior, compile.Contract!.BehaviorContractId);
        Assert.Equal(1, compile.Contract.ContractMajorVersion);
        Assert.Equal(
            ExpectedCaseIds,
            compile.Contract.Cases.Select(static item => item.CaseId).Order(StringComparer.Ordinal));
        Assert.Contains("\"oneOf\"", compile.Contract.OneOfSchemaJson, StringComparison.Ordinal);
        Assert.Contains("case.ManualResearchRequest", compile.Contract.OneOfSchemaJson, StringComparison.Ordinal);
        Assert.Contains("case.GmailMessageReceived", compile.Contract.OneOfSchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Runtime.CompilerServices.IUnion", compile.Contract.OneOfSchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain(", Version=", compile.Contract.OneOfSchemaJson, StringComparison.Ordinal);

        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var behavior = test.Neuron<IBehaviorNeuron>(BehaviorsFixture.SampleBehavior);

        var active = await InstallAsync(test, behavior, program, "multi-case-union");
        Assert.Equal(BehaviorRevisionStatus.Active, active.Status);
        Assert.False(string.IsNullOrWhiteSpace(active.ActiveArtifactHash));
        Assert.True(active.ActivationGateOpen);

        var manualTask = test.Neuron<ITask>("multi-case-manual-task");
        var manualWorker = test.Neuron<IWorker>("multi-case-manual-worker");
        var gmailTask = test.Neuron<ITask>("multi-case-gmail-task");
        var gmailWorker = test.Neuron<IWorker>("multi-case-gmail-worker");

        var manualBound = await ActivateCaseAsync(
            behavior,
            active.ActiveArtifactHash!,
            manualTask.Id,
            manualWorker.Id,
            "case.ManualResearchRequest",
            protectedPayload: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        Assert.Equal(TaskState.Pending, manualBound.State);
        Assert.Equal("case.ManualResearchRequest", manualBound.Activation!.CaseId);
        Assert.Equal("ManualResearchRequest", manualBound.Activation.TriggerTypeName);

        var gmailBound = await ActivateCaseAsync(
            behavior,
            active.ActiveArtifactHash!,
            gmailTask.Id,
            gmailWorker.Id,
            "case.GmailMessageReceived",
            protectedPayload: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        Assert.Equal(TaskState.Pending, gmailBound.State);
        Assert.Equal("case.GmailMessageReceived", gmailBound.Activation!.CaseId);
        Assert.Equal("GmailMessageReceived", gmailBound.Activation.TriggerTypeName);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ActivateCaseAsync(
                behavior,
                active.ActiveArtifactHash!,
                test.Neuron<ITask>("multi-case-unknown-task").Id,
                test.Neuron<IWorker>("multi-case-unknown-worker").Id,
                "case.DoesNotExist",
                protectedPayload: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")));

        var snapshot = await behavior.Reference.Read();
        Assert.Equal(2, snapshot.ActiveTaskCount);
        Assert.NotNull(snapshot.Bindings);
        Assert.Equal(
            ExpectedCaseIds,
            snapshot.Bindings!
                .Select(static item => item.TargetCase)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    private static async Task<BoundBehaviorActivationResult> ActivateCaseAsync(
        TestNeuron<IBehaviorNeuron> behavior,
        string artifactHash,
        NeuronId taskId,
        NeuronId workerId,
        string caseId,
        Guid protectedPayload)
    {
        var binding = BehaviorActivationBindings.ForExistingTask(
            taskId,
            workerId,
            new BehaviorId(BehaviorsFixture.SampleBehavior),
            new BehaviorRevisionId(artifactHash),
            contractVersion: "1",
            caseId: caseId,
            protectedPayload: new ProtectedPayloadReference(protectedPayload));

        return await behavior.Reference.ActivateBound(
            new ActivateBoundBehavior(CommandId.New(), artifactHash, binding));
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
        Assert.Equal(BehaviorRevisionStatus.Proposed, proposed.Status);
        Assert.False(string.IsNullOrWhiteSpace(proposed.ProposedArtifactHash));
        Assert.Null(proposed.LastCompileFailure);

        var tested = await behavior.Reference.RunTests(
            new RunBehaviorTests(CommandId.New(), proposed.ProposedArtifactHash!));
        Assert.Equal(BehaviorRevisionStatus.TestsPassed, tested.Status);
        Assert.True(tested.TestsPassed);

        var approval = new BehaviorRevisionApproval(
            Guid.NewGuid(),
            CommandId.New(),
            proposed.ProposedArtifactHash!,
            ISessionNeuron.ForOwner(test.Client.Owner),
            test.Clock.UtcNow);
        var wait = behavior.Incoming.NextAsync<BehaviorRevisionApproval>(TestContext.Current.CancellationToken);
        await test.Client.SendAsync(behavior.Id, approval, TestContext.Current.CancellationToken);
        _ = await wait;
        await behavior.Reference.Approve(approval);
        return await behavior.Reference.Activate(
            new ActivateBehaviorRevision(CommandId.New(), proposed.ProposedArtifactHash!));
    }
}
