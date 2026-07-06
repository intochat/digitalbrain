using DigitalBrain.Core;
using DigitalBrain.Kernel.SelfEvolution;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Marketplace.Contracts;

namespace DigitalBrain.Kernel;

public sealed class MarketplaceInstallApplyHandler(
    IGrainFactory grains,
    IServiceProvider services,
    ILogger<MarketplaceInstallApplyHandler> logger) : ISelfEvolutionApplyHandler
{
    public const string ApplyViaId = "marketplace.install";

    public string ApplyVia => ApplyViaId;
    public SelfEvolutionRisk MaxRisk => SelfEvolutionRisk.PackInstall;

    public async Task<SelfEvolutionApplyResult> ApplyAsync(SelfEvolutionProposal proposal, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var staged = await FindStagedInstallAsync(proposal);
        if (staged is null)
        {
            return new SelfEvolutionApplyResult(
                proposal.ProposalId,
                proposal.ApplyVia,
                Succeeded: false,
                $"No staged marketplace install was found for proposal '{proposal.ProposalId}'.");
        }

        var marketplace = grains.GetGrain<IMarketplaceNeuron>(staged.MarketplaceNeuronId);
        await MarketplaceInstallActivation.ApplyAsync(
            staged,
            synapse => marketplace.FireAsync(synapse),
            grains,
            services.GetService<HomeFeedBus>(),
            logger);

        return new SelfEvolutionApplyResult(
            proposal.ProposalId,
            proposal.ApplyVia,
            Succeeded: true,
            $"Activated marketplace pack {staged.Pack.Name}@{staged.Pack.Version}.");
    }

    private async Task<MarketplaceInstallStaged?> FindStagedInstallAsync(SelfEvolutionProposal proposal)
    {
        if (string.IsNullOrWhiteSpace(proposal.Origin))
        {
            return null;
        }

        var marketplace = grains.GetGrain<IMarketplaceNeuron>(proposal.Origin);
        var timeline = await marketplace.GetOutgoingTimelineAsync();
        return timeline
            .OfType<MarketplaceInstallStaged>()
            .LastOrDefault(staged => string.Equals(staged.ProposalId, proposal.ProposalId, StringComparison.Ordinal));
    }
}

internal static class MarketplaceInstallActivation
{
    public static async Task ApplyAsync(
        MarketplaceInstallStaged staged,
        Func<Synapse, ValueTask> fireMarketplaceAsync,
        IGrainFactory grains,
        HomeFeedBus? bus,
        ILogger logger)
    {
        var pack = staged.Pack;

        await fireMarketplaceAsync(new CommissionTaken(
            pack.Name,
            pack.Version,
            staged.BuyerId,
            pack.OwnerId,
            pack.CommissionRate,
            staged.CommissionAmount));

        await fireMarketplaceAsync(new NeuroPackInstalled(pack));

        if (string.Equals(pack.Name, KernelPack.Name, StringComparison.OrdinalIgnoreCase))
        {
            var aspire = grains.GetGrain<IAspireNeuron>("aspire-main");
            await aspire.FireAsync(new PerformKernelSelfUpdate(pack.Version));
        }

        var genKey = "generated-" + pack.Name.ToLowerInvariant();
        var generated = grains.GetGrain<IGeneratedNeuron>(genKey);
        await generated.DeliverAsync(new NeuroPackInstalled(pack));
        await generated.FireAsync(new ExperienceUsed(pack.Name, "installed-and-activated", staged.BuyerId, staged.SessionId));

        var published = new List<NeuroPack> { pack };
        var installed = new List<NeuroPack> { pack };
        var installedSurface = MarketplaceUiSurfaces.InstalledBundlesFromPacks(
            published,
            installed,
            staged.BuyerId,
            staged.SessionId);
        await fireMarketplaceAsync(installedSurface);

        if (bus is not null)
        {
            await bus.BroadcastAsync(UiSurfaceRfwBridge.FromUiSurface(installedSurface, staged.MarketplaceNeuronId));
        }

        logger.LogInformation(
            "Marketplace INSTALL {Key} by {Buyer}. Commission {Rate:P0} taken for seller {Seller}.",
            pack.Name + "@" + pack.Version,
            staged.BuyerId,
            pack.CommissionRate,
            pack.OwnerId);
    }
}


