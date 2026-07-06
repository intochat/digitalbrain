using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests.Kernel;

public sealed class MarketplaceInstallApprovalTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) => builder.ConfigureServices(services =>
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Marketplace:RejectUnsignedPacks"] = "false",
                ["DigitalBrain:Marketplace:TrustedLocalInstallBypass"] = "false"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
    });
    [Fact]
    public async Task Install_Stages_Approval_And_Does_Not_Activate_Before_Decision()
    {
        var (market, approval, staged) = await StageInstallAsync("GatePackPending", "market-gate-pending", "buyer-1");

        var marketTimeline = await market.GetOutgoingTimelineAsync();
        Assert.Contains(marketTimeline.OfType<MarketplaceInstallStaged>(), item => item.ProposalId == staged.ProposalId);
        Assert.DoesNotContain(marketTimeline, synapse => synapse is NeuroPackInstalled);
        Assert.DoesNotContain(marketTimeline, synapse => synapse is CommissionTaken);

        var approvalTimeline = await approval.GetOutgoingTimelineAsync();
        Assert.Contains(approvalTimeline.OfType<SelfEvolutionProposalPending>(), pending =>
            pending.ProposalId == staged.ProposalId
            && pending.ApplyVia == MarketplaceInstallApplyHandler.ApplyViaId
            && pending.Risk == SelfEvolutionRisk.PackInstall);
    }

    [Fact]
    public async Task Rejected_Install_Proposal_Does_Not_Activate_Pack()
    {
        var (market, approval, staged) = await StageInstallAsync("GatePackReject", "market-gate-reject", "buyer-2");

        await approval.DeliverAsync(new SelfEvolutionDecision(staged.ProposalId, Approved: false, DecidedBy: "user:owner", Reason: "deny"));

        var marketTimeline = await market.GetOutgoingTimelineAsync();
        Assert.DoesNotContain(marketTimeline, synapse => synapse is NeuroPackInstalled);
        Assert.DoesNotContain(marketTimeline, synapse => synapse is CommissionTaken);
    }

    [Fact]
    public async Task Approved_Install_Proposal_Activates_Existing_Marketplace_Behavior()
    {
        var (market, approval, staged) = await StageInstallAsync("GatePackApprove", "market-gate-approve", "buyer-3");

        await approval.DeliverAsync(new SelfEvolutionDecision(staged.ProposalId, Approved: true, DecidedBy: "user:owner"));

        var marketTimeline = await market.GetOutgoingTimelineAsync();
        Assert.Contains(marketTimeline.OfType<CommissionTaken>(), commission =>
            commission.PackName == "GatePackApprove" && commission.BuyerId == "buyer-3");
        Assert.Contains(marketTimeline.OfType<NeuroPackInstalled>(), installed => installed.Pack.Name == "GatePackApprove");

        var generated = Grain<IGeneratedNeuron>("generated-gatepackapprove");
        var generatedIncoming = await generated.GetIncomingTimelineAsync();
        Assert.Contains(generatedIncoming.OfType<NeuroPackInstalled>(), installed => installed.Pack.Name == "GatePackApprove");

        var approvalTimeline = await approval.GetOutgoingTimelineAsync();
        Assert.Contains(approvalTimeline.OfType<SelfEvolutionApplyResult>(), result =>
            result.ProposalId == staged.ProposalId && result.Succeeded);
    }

    private async Task<(IMarketplaceNeuron Market, ISelfEvolutionNeuron Approval, MarketplaceInstallStaged Staged)> StageInstallAsync(
        string packName,
        string marketId,
        string buyerId)
    {
        var market = Grain<IMarketplaceNeuron>(marketId);
        await market.FireAsync(new PublishToMarketplace(packName, "1.0", Code: "public sealed class Pack { }", OwnerId: "seller", IsPrivate: false, CommissionRate: 0.15));
        await market.FireAsync(new InstallFromMarketplace(packName, "1.0", BuyerId: buyerId));

        var staged = Assert.Single(
            (await market.GetOutgoingTimelineAsync()).OfType<MarketplaceInstallStaged>(),
            item => item.Pack.Name == packName && item.BuyerId == buyerId);
        var approval = Grain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        return (market, approval, staged);
    }

    public sealed class TrustedLocalBypassTests : NeuronTestBase
    {
        protected override void ConfigureSilo(ISiloBuilder builder) => builder.ConfigureServices(services =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DigitalBrain:Marketplace:RejectUnsignedPacks"] = "false",
                    ["DigitalBrain:Marketplace:TrustedLocalInstallBypass"] = "true"
                })
                .Build();
            services.AddSingleton<IConfiguration>(configuration);
        });

        [Fact]
        public async Task Trusted_Local_Bypass_Activates_Without_Pending_Approval()
        {
            var market = Grain<IMarketplaceNeuron>("market-gate-bypass");
            await market.FireAsync(new PublishToMarketplace("GatePackBypass", "1.0", Code: "public sealed class Pack { }", OwnerId: "seller"));
            await market.FireAsync(new InstallFromMarketplace("GatePackBypass", "1.0", BuyerId: "local-dev"));

            var marketTimeline = await market.GetOutgoingTimelineAsync();
            Assert.Contains(marketTimeline.OfType<MarketplaceInstallStaged>(), staged => staged.Pack.Name == "GatePackBypass");
            Assert.Contains(marketTimeline.OfType<NeuroPackInstalled>(), installed => installed.Pack.Name == "GatePackBypass");

            var approval = Grain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
            var approvalTimeline = await approval.GetOutgoingTimelineAsync();
            Assert.DoesNotContain(approvalTimeline.OfType<SelfEvolutionProposalPending>(), pending =>
                pending.ApplyVia == MarketplaceInstallApplyHandler.ApplyViaId);
        }
    }
}

