extern alias McpProject;

using DigitalBrain.Core;
using DigitalBrain.Kernel.Foundry;
using McpProject::DigitalBrain.Mcp;
using DigitalBrain.Tests.TestSupport;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests.Foundry;

public sealed class CodeFoundryApprovalTests : NeuronTestBase
{
    private readonly FakeBuildRunner _buildRunner = new();
    private readonly FakeResourceController _resourceController = new();

    protected override void ConfigureSilo(ISiloBuilder builder)
    {
        builder.AddFoundry();
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IBuildRunner>(_buildRunner);
            services.AddSingleton<IResourceController>(_resourceController);
        });
    }

    [Fact]
    public async Task RunCodeFoundry_Default_Stages_Approval_Without_Running_Generated_Code()
    {
        var tools = new DigitalBrainMutationTools(new TestGrainFactory(this));

        var result = await tools.RunCodeFoundry("return approval default");

        Assert.Equal("FoundryRequest accepted (no terminal synapse yet).", result);

        var foundry = Grain<ICodeFoundryLoopNeuron>("foundry-main");
        var staged = Assert.Single((await foundry.GetOutgoingTimelineAsync()).OfType<FoundryApplyStaged>());
        Assert.Equal(TargetTier.Run, staged.Tier);

        var approval = Grain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        var approvalTimeline = await approval.GetOutgoingTimelineAsync();
        Assert.Contains(approvalTimeline.OfType<SelfEvolutionProposalPending>(), pending =>
            pending.ProposalId == staged.ProposalId
            && pending.ApplyVia == SelfEvolutionApplyVia.FoundryRun
            && pending.Risk == SelfEvolutionRisk.InProcessCode);

        var runner = Grain<ICodeRunNeuron>("foundry-coderun");
        Assert.DoesNotContain(await runner.GetOutgoingTimelineAsync(), synapse => synapse is CodeRunResult);
    }

    [Fact]
    public async Task AutoApply_Is_Rejected_Unless_Trusted_Config_Enables_It()
    {
        var foundry = Grain<ICodeFoundryLoopNeuron>("foundry-autogate");

        await foundry.FireAsync(new FoundryRequest("auto apply attempt", TargetTier.Run, AutoApply: true));

        var timeline = await foundry.GetOutgoingTimelineAsync();
        Assert.Contains(timeline.OfType<FoundryRolledBack>(), rolledBack =>
            rolledBack.Reason == "auto-apply-requires-trusted-config");
        Assert.DoesNotContain(timeline, synapse => synapse is FoundryApplyStaged);
    }

    [Fact]
    public async Task Approved_Run_Proposal_Executes_Generated_Code()
    {
        var foundry = Grain<ICodeFoundryLoopNeuron>("foundry-run-approval");
        await foundry.FireAsync(new FoundryRequest("approved run path", TargetTier.Run));

        var staged = Assert.Single((await foundry.GetOutgoingTimelineAsync()).OfType<FoundryApplyStaged>());

        var approval = Grain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        await approval.DeliverAsync(new SelfEvolutionDecision(staged.ProposalId, Approved: true, DecidedBy: "user:owner"));

        var runner = Grain<ICodeRunNeuron>("foundry-coderun");
        Assert.Contains((await runner.GetOutgoingTimelineAsync()).OfType<CodeRunResult>(), result =>
            result.Success && result.Output.Contains("fallback: approved run path", StringComparison.Ordinal));

        var foundryTimeline = await foundry.GetOutgoingTimelineAsync();
        Assert.Contains(foundryTimeline.OfType<FoundryCompleted>(), completed =>
            completed.Spec == "approved run path"
            && completed.Tier == TargetTier.Run
            && completed.Applied);
    }

    [Fact]
    public async Task Approved_Deploy_Proposal_Builds_And_Requests_Restart()
    {
        var foundry = Grain<ICodeFoundryLoopNeuron>("foundry-deploy-approval");
        await foundry.FireAsync(new FoundryRequest("approved deploy path", TargetTier.Deploy));

        var staged = Assert.Single((await foundry.GetOutgoingTimelineAsync()).OfType<FoundryApplyStaged>());
        Assert.Equal(SelfEvolutionApplyVia.FoundryDeploy, (await PendingProposalAsync(staged.ProposalId)).ApplyVia);

        var approval = Grain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        await approval.DeliverAsync(new SelfEvolutionDecision(staged.ProposalId, Approved: true, DecidedBy: "user:owner"));

        Assert.Equal(1, _buildRunner.Calls);
        Assert.Equal(1, _resourceController.Restarts);

        var deployer = Grain<ICodeDeployNeuron>("foundry-codedeploy");
        Assert.Contains((await deployer.GetOutgoingTimelineAsync()).OfType<CodeBuilt>(), built =>
            built.ModuleName == staged.ModuleName && built.Success);

        var foundryTimeline = await foundry.GetOutgoingTimelineAsync();
        Assert.Contains(foundryTimeline.OfType<FoundryCompleted>(), completed =>
            completed.Spec == "approved deploy path"
            && completed.Tier == TargetTier.Deploy
            && completed.Applied);
    }

    [Fact]
    public async Task Failed_Deploy_Proposal_Emits_Rollback_Audit_With_Checkpoint()
    {
        _buildRunner.NextResult = false;
        _buildRunner.NextLog = "build failed";
        var foundry = Grain<ICodeFoundryLoopNeuron>("foundry-deploy-failure");
        await foundry.FireAsync(new FoundryRequest("failed deploy path", TargetTier.Deploy));

        var staged = Assert.Single((await foundry.GetOutgoingTimelineAsync()).OfType<FoundryApplyStaged>());
        var approval = Grain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        await approval.DeliverAsync(new SelfEvolutionDecision(staged.ProposalId, Approved: true, DecidedBy: "user:owner"));

        Assert.Equal(1, _buildRunner.Calls);
        Assert.Equal(0, _resourceController.Restarts);

        var foundryTimeline = await foundry.GetOutgoingTimelineAsync();
        Assert.Contains(foundryTimeline.OfType<FoundryRolledBack>(), rolledBack =>
            rolledBack.Spec == "failed deploy path"
            && rolledBack.Reason == "build"
            && rolledBack.CheckpointId == staged.CheckpointId);

        var approvalTimeline = await approval.GetOutgoingTimelineAsync();
        Assert.Contains(approvalTimeline.OfType<SelfEvolutionApplyResult>(), result =>
            result.ProposalId == staged.ProposalId
            && !result.Succeeded
            && result.RollbackCheckpointId == staged.CheckpointId);
        Assert.Contains(approvalTimeline.OfType<SelfEvolutionRollbackRequired>(), rollback =>
            rollback.ProposalId == staged.ProposalId
            && rollback.ApplyVia == SelfEvolutionApplyVia.FoundryDeploy
            && rollback.CheckpointId == staged.CheckpointId);
    }
    private async Task<SelfEvolutionProposalPending> PendingProposalAsync(string proposalId)
    {
        var approval = Grain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        return (await approval.GetOutgoingTimelineAsync())
            .OfType<SelfEvolutionProposalPending>()
            .Single(pending => pending.ProposalId == proposalId);
    }
}

