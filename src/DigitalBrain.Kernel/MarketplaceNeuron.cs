using DigitalBrain.Core;

using DigitalBrain.Core.Trust;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.SelfEvolution;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Marketplace.Contracts;
using Microsoft.Extensions.AI;
namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.marketplace.v1")]
public class MarketplaceNeuron(ILogger<MarketplaceNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IMarketplaceNeuron
{
    private Dictionary<string, NeuroPack>? _publishedCache;

    public async Task HandleAsync(PublishToMarketplace cmd)
    {
        if (GatePublishing && !PublisherTrust.IsTrusted(ToNeuroPack(cmd), TrustedPublisherKeys()))
        {
            Logger.LogWarning("Publish REJECTED - pack {Name}@{Ver} is not from a trusted publisher (publishing gate enabled)",
                cmd.PackName, cmd.Version);
            return;
        }

        Logger.LogInformation("Marketplace PUBLISHED real pack {Name}@{Ver} owner={Owner} private={Private} commission={Rate:P0}",
            cmd.PackName, cmd.Version, cmd.OwnerId, cmd.IsPrivate, cmd.CommissionRate);

        EnsureCache();
        _publishedCache![KeyFor(cmd.PackName, cmd.Version)] = MaterializeManifest(ToNeuroPack(cmd));

        var bus = ServiceProvider.GetService<HomeFeedBus>();
        var published = _publishedCache!.Values.ToList();
        var listSurface = MarketplaceUiSurfaces.MarketplaceListFromPacks(published, published);
        await FireAsync(listSurface);
        if (bus != null)
        {
            await bus.BroadcastAsync(UiSurfaceRfwBridge.FromUiSurface(listSurface, Self.Value));
        }
    }

    private static string KeyFor(string name, string version) => $"{name}@{version}";

    private static NeuroPack ToNeuroPack(PublishToMarketplace p) =>
        new(p.PackName, p.Version, p.OwnerId, p.IsPrivate, p.CommissionRate, p.Code, p.Description, p.AuthorPublicKeyBase64, p.SignatureBase64, p.Price);

    private NeuroPack MaterializeManifest(NeuroPack pack)
    {
        if (pack.Manifest is not null) return pack;
        if (string.IsNullOrEmpty(pack.Code)) return pack;

        var embodiment = ServiceProvider.GetService<IPackEmbodiment>();
        if (embodiment is null) return pack;

        try
        {
            using var embodied = embodiment.Embody(pack.Name, pack.Code);
            var manifest = embodied.GetBundleManifest();
            return manifest is null ? pack : pack with { Manifest = manifest };
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Manifest materialization failed for pack {Name}@{Ver}; listing without bundle metadata",
                pack.Name, pack.Version);
            return pack;
        }
    }

    public async Task HandleAsync(InstallFromMarketplace cmd)
    {
        var pack = FindPublishedPack(cmd.PackName, cmd.Version);
        if (pack == null)
        {
            Logger.LogWarning("Install failed - pack not found: {Key}", cmd.PackName + "@" + cmd.Version);
            return;
        }

        if (pack.IsPrivate && cmd.BuyerId != pack.OwnerId)
        {
            Logger.LogWarning("Install blocked - pack {Key} is private to owner {Owner}", cmd.PackName + "@" + cmd.Version, pack.OwnerId);
            return;
        }

        var isSigned = !string.IsNullOrEmpty(pack.AuthorPublicKeyBase64) && !string.IsNullOrEmpty(pack.SignatureBase64);
        if (isSigned)
        {
            if (!PackSignatureVerifier.VerifyPack(pack))
            {
                Logger.LogWarning("Install REJECTED - invalid signature on pack {Key}", cmd.PackName + "@" + cmd.Version);
                return;
            }
            Logger.LogInformation("Install signature verified for pack {Key}", cmd.PackName + "@" + cmd.Version);
        }
        else if (RejectUnsignedPacks)
        {
            Logger.LogWarning("Install REJECTED - pack {Key} is unsigned and unsigned installs are disabled", cmd.PackName + "@" + cmd.Version);
            return;
        }
        else
        {
            Logger.LogWarning("Install WARNING - pack {Key} is unsigned (allowed during trust transition)", cmd.PackName + "@" + cmd.Version);
        }

        // Economics / license check removed as trash (premium entitlement flow not core to current Ino focus)

        var commissionAmount = 0.0;
        var staged = new MarketplaceInstallStaged(
            ProposalId: "marketplace-install-" + Guid.NewGuid().ToString("N"),
            MarketplaceNeuronId: Self.Value,
            Pack: pack,
            BuyerId: cmd.BuyerId,
            SessionId: cmd.SessionId,
            CommissionAmount: commissionAmount);
        await FireAsync(staged);

        if (TrustedLocalInstallBypass)
        {
            await FireAsync(new AuditBypass("TrustedLocalInstallBypass", $"Unsigned/ local install bypass for {pack.Name}@{pack.Version}", DateTimeOffset.UtcNow));
            await MarketplaceInstallActivation.ApplyAsync(
                staged,
                synapse => FireAsync(synapse),
                GrainFactory,
                ServiceProvider.GetService<HomeFeedBus>(),
                Logger);
            return;
        }

        var proposal = new SelfEvolutionProposal(
            ProposalId: staged.ProposalId,
            Scope: "marketplace",
            Rationale: $"Install marketplace pack {pack.Name}@{pack.Version} for {cmd.BuyerId}.",
            ProposedChange: $"Activate pack {pack.Name}@{pack.Version} and deliver it to the generated-neuron host.",
            ApplyVia: MarketplaceInstallApplyHandler.ApplyViaId,
            Risk: SelfEvolutionRisk.PackInstall,
            RequiresHumanApproval: true,
            RollbackPlan: "Remove the generated pack activation and restore the previous installed-bundles surface if verification fails.",
            Origin: Self.Value)
        {
            Sender = Self,
            Receiver = new NeuronId(SelfEvolutionNeuronIds.Main),
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = CurrentCause?.CorrelationId ?? CurrentCause?.SynapseId,
            CausationId = CurrentCause?.SynapseId
        };

        await GrainFactory.GetGrain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main).DeliverAsync(proposal);
        Logger.LogInformation("Marketplace install staged for approval: {Key} by {Buyer} proposal={ProposalId}",
            cmd.PackName + "@" + cmd.Version, cmd.BuyerId, staged.ProposalId);
    }

    public async Task HandleAsync(ListPublished _cmd)
    {
        var packs = GetPublishedPacks();
        Logger.LogInformation("Marketplace listing {Count} real packs", packs.Count);
        await FireAsync(new PublishedList(packs));
    }

    public async Task HandleAsync(FilterMarketplace cmd)
    {
        var published = GetPublishedPacks();
        var surface = MarketplaceUiSurfaces.MarketplaceTreeSurface(
            published, Array.Empty<NeuroPack>(), cmd.Tier, cmd.Channel, Self.Value);
        await FireAsync(surface);

        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus is not null)
            await bus.BroadcastAsync(UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value));
    }

    private IReadOnlyList<NeuroPack> GetPublishedPacks()
    {
        EnsureCache();
        return _publishedCache!.Values.ToList();
    }

    private void EnsureCache()
    {
        if (_publishedCache is not null) return;

        _publishedCache = new Dictionary<string, NeuroPack>(StringComparer.OrdinalIgnoreCase);

        var gated = GatePublishing;
        var trustedKeys = gated ? TrustedPublisherKeys() : null;
        foreach (var p in OutgoingJournal.Concat(IncomingJournal).OfType<PublishToMarketplace>())
        {
            var pack = ToNeuroPack(p);
            if (gated && !PublisherTrust.IsTrusted(pack, trustedKeys!)) continue;
            _publishedCache[KeyFor(p.PackName, p.Version)] = pack;
        }

        foreach (var s in MarketplaceSeeds.LocalUiPacks)
        {
            var k = KeyFor(s.Name, s.Version);
            if (!_publishedCache.ContainsKey(k)) _publishedCache[k] = MaterializeManifest(s);
        }
    }

    private NeuroPack? FindPublishedPack(string name, string version)
    {
        EnsureCache();
        _publishedCache!.TryGetValue(KeyFor(name, version), out var p);
        return p;
    }

    private bool RejectUnsignedPacks =>
        ServiceProvider.GetService<IConfiguration>()?.GetValue("DigitalBrain:Marketplace:RejectUnsignedPacks", true) ?? true;

    private bool TrustedLocalInstallBypass =>
        ServiceProvider.GetService<IConfiguration>()?.GetValue("DigitalBrain:Marketplace:TrustedLocalInstallBypass", false) ?? false;
    private bool GatePublishing =>
        ServiceProvider.GetService<IConfiguration>()?.GetValue("DigitalBrain:Marketplace:GatePublishing", false) ?? false;

    private IReadOnlyCollection<string> TrustedPublisherKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal) { TrustedPublisher.PublicKeyBase64 };
        var configured = ServiceProvider.GetService<IConfiguration>()
            ?.GetSection("DigitalBrain:Marketplace:TrustedPublisherKeys").Get<string[]>();
        if (configured is not null)
            foreach (var key in configured) keys.Add(key);
        return keys;
    }

}

