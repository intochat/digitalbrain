using DigitalBrain.Core;
using DigitalBrain.Ino;
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
            synapse => marketplace.FireAsync(synapse, ct),
            grains,
            services.GetService<HomeFeedBus>(),
            logger,
            ct);

        // Register installed pack as capability for modern intent classifier / vector search
        var cap = new InoIntentClassifier.Capability(
            staged.Pack.Name,
            $"Pack {staged.Pack.Name} v{staged.Pack.Version}: {staged.Pack.Description}",
            new[] { $"use {staged.Pack.Name}", staged.Pack.Name.ToLowerInvariant() },
            "pack");
        InoIntentClassifier.RegisterCapability(cap);

        await marketplace.FireAsync(new CapabilityRegistered(cap.Id, cap.Description, cap.Examples, cap.Tier, staged.Pack.Name), ct);

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
        Func<Synapse, Task> fireMarketplaceAsync,
        IGrainFactory grains,
        HomeFeedBus? bus,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            await aspire.FireAsync(new PerformKernelSelfUpdate(pack.Version), cancellationToken);
        }

        var genKey = "generated-" + pack.Name.ToLowerInvariant();
        var generated = grains.GetGrain<IGeneratedNeuron>(genKey);
        await generated.DeliverAsync(new NeuroPackInstalled(pack), cancellationToken);
        await generated.FireAsync(new ExperienceUsed(pack.Name, "installed-and-activated", staged.BuyerId, staged.SessionId), cancellationToken);

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
            await bus.BroadcastAsync(UiSurfaceRfwBridge.FromUiSurface(installedSurface, staged.MarketplaceNeuronId), cancellationToken);
        }

        logger.LogInformation(
            "Marketplace INSTALL {Key} by {Buyer}. Commission {Rate:P0} taken for seller {Seller}.",
            pack.Name + "@" + pack.Version,
            staged.BuyerId,
            pack.CommissionRate,
            pack.OwnerId);
    }
}


