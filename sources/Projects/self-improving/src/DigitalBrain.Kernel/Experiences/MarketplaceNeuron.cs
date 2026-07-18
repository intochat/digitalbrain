using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Protocol;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Distribution;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;
using DigitalBrain.Kernel.Experiences;
using DigitalBrain.Hosting.DigitalBrain;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace DigitalBrain.Kernel;

[GenerateSerializer]
public sealed class MarketplaceState
{
    [Id(0)]
    public Dictionary<string, ExperienceListing> Listings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [Id(1)]
    public Dictionary<string, string> PackagePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // E8 Discovery: persisted peers (re-introduced per ROADMAP; was deleted as "unused" in Phase 0, now needed for /market scan + peer health).
    [Id(2)]
    public Dictionary<string, PeerInfo> Peers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // GlobalBrain: persisted well-known hosted marketplace peer (sync path for LAN kernels; telemetry for ino).
    [Id(3)]
    public GlobalPeer? GlobalPeer { get; set; }

    [Id(4)]
    public Dictionary<string, ExperienceListing> GlobalListings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [Id(5)]
    public Dictionary<string, string> GlobalPackagePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [Id(6)]
    public Dictionary<string, ExperienceRating[]> Ratings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [Id(7)]
    public List<string> InstalledIds { get; set; } = new();

    [Id(8)]
    public List<string> RuleCapableInstalledIds { get; set; } = new();
}

[GenerateSerializer]
public sealed record PeerInfo(
    [property: Id(0)] string World,
    [property: Id(1)] string Address, // host:gatewayPort
    [property: Id(2)] DateTimeOffset LastSeen,
    [property: Id(3)] bool Healthy = true);

[GrainType("marketplace")]
public sealed class MarketplaceNeuron : Neuron, IMarketplace, IHandle<UpdateBundle>, IHandle<StartQuarantineWorld>, IHandle<SyncListingsToGlobal>, IHandle<PullPopularFromGlobal>, IHandle<RateExperience>, IHandle<BundleUninstalled>, IHandle<RunDistributionSimulation>, IHandle<ListPublished>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IPersistentState<MarketplaceState> _state;

    public MarketplaceNeuron(
        [PersistentState("marketplace", "Default")] IPersistentState<MarketplaceState> state)
        : base()
    {
        _state = state;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        var domain = Domain;
        var discoveryEnv = Environment.GetEnvironmentVariable("DIGITALBRAIN_DISCOVERY");
        // DIGITALBRAIN_DISCOVERY=on/off gates the beacon when set by the manifest (faithful boot).
        // Fall back to legacy DIGITALBRAIN_PEER_DISCOVERY for kernels not launched via brain.ino.
        var doBeacon = string.Equals(discoveryEnv, "on", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(discoveryEnv)
                && (string.Equals(domain, "root", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DIGITALBRAIN_PEER_DISCOVERY"))));
        if (doBeacon)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var udp = new System.Net.Sockets.UdpClient();
                    var beacon = Encoding.UTF8.GetBytes($"digitalbrain-market {domain} {Environment.MachineName}:30000");
                    while (true)
                    {
                        try { await udp.SendAsync(beacon, new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, 30001)); } catch { }
                        await Task.Delay(15000);
                    }
                }
                catch { }
            }, cancellationToken);
        }

        // U5 / hygiene: seed L1 capsules for key kernel experiences (google-auth, llm-agent) from pa-files/marketplace so they are visible in marketplace as L1 (descriptor + contract, no compiled behavior change).
        // Task2: extend to full osInoIdsToEnsurePacked via packAndPublishIfMissing (real .brain from os/*.ino inoContent for faithful packing + always sig) when not pre-seeded. Seed for root and global (WellKnown) mkt keys (tests/launcher use "global").
        // Fire-and-forget (like beacon) to avoid activate blocking / reentrancy under TestCluster load; core packing still happens for completeness (ref Task1 reviews: await/forget justified by reentrancy, no core E impact).
        if (string.Equals(domain, "root", StringComparison.OrdinalIgnoreCase) || string.Equals(domain, Brain.WellKnownKey, StringComparison.OrdinalIgnoreCase))
        {
            await SeedL1CapsuleIfMissing("google-auth", cancellationToken); // T2 connectors: "google-auth" bundle id string unchanged (impl grain now GoogleAuthConnectorNeuron in Connectors; GrainType + L1 seed from pa intact per scoped wiring).
            await SeedL1CapsuleIfMissing("llm-agent", cancellationToken); // F T2: llm-agent (LlmAgentNeuron + tools) extracted to DigitalBrain.Ino; L1 .brain seed + string id unchanged.
            _ = Task.Run(async () =>
            {
                try { await EnsureAllOsInoBundlesPackedAsync(CancellationToken.None); }
                catch { }
            });
        }

        // Ensure the 'marketplace' surface is emitted on activation so shell nav "click marketplace" (PinSurface for 'marketplace') finds it in _recentSurfaces and renders the listings card + install/run buttons in main region immediately.
        // Dynamic refreshes on ExperienceListed etc. still driven by .ino/yaml rules (single source); this seeds initial view so "marketplace shows nothing on click" is resolved. gRPC flutter and hex1b both benefit (cached in SurfaceStreamService + shell recent).
        // Distribution N+1 / RunExperience paths unchanged (high-sev covers).
        var initialMarketSurf = ListingsSurface();
        await Emit(initialMarketSurf);
        SurfaceStreamService.Publish(SurfaceStreamService.ToMessage(initialMarketSurf));
    }

    private async Task SeedL1CapsuleIfMissing(string id, CancellationToken cancellationToken)
    {
        if (_state.State.Listings.ContainsKey(id)) return;
        var path = Path.Combine("pa-files", "marketplace", $"{id}.brain");
        if (!File.Exists(path)) return;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var manifest = ReadManifest(bytes);
            if (manifest != null)
            {
                await AddListingAsync(manifest, bytes, cancellationToken);
            }
        }
        catch { }
    }



    private string Domain => this.GetPrimaryKeyString();

    private string StoreDirectory => Path.Combine("pa-files", "marketplace", Sanitize(Domain));

    private const string GlobalPeerAddressDefault = "globalbrain:30000";

    private bool IsGlobalPeerAddress(string? addr) =>
        !string.IsNullOrWhiteSpace(addr) && (addr.Contains("global", StringComparison.OrdinalIgnoreCase) || addr == (_state.State.GlobalPeer?.Address ?? GlobalPeerAddressDefault));

    private async Task<byte[]?> GetGlobalPackageBytesAsync(string experienceId, CancellationToken cancellationToken)
    {
        if (!_state.State.GlobalPackagePaths.TryGetValue(experienceId, out var path) || !File.Exists(path))
            return null;
        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    public Task<IReadOnlyList<ExperienceListing>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExperienceListing>>(
            _state.State.Listings.Values.OrderByDescending(l => l.PublishedAt).ToList());

    public async Task<byte[]?> GetPackageBytesAsync(string experienceId, CancellationToken cancellationToken = default)
    {
        if (!_state.State.PackagePaths.TryGetValue(experienceId, out var path) || !File.Exists(path))
            return null;
        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    public async Task<ExperienceListing> AddListingAsync(ExperienceManifest manifest, byte[] packageBytes, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(StoreDirectory);
        var path = Path.Combine(StoreDirectory, $"{Sanitize(manifest.Id)}{ExperiencePackageFormat.Extension}");
        await File.WriteAllBytesAsync(path, packageBytes, cancellationToken);

        var listing = new ExperienceListing(manifest, packageBytes.LongLength, DateTimeOffset.UtcNow);
        _state.State.Listings[manifest.Id] = listing;
        _state.State.PackagePaths[manifest.Id] = path;
        await _state.WriteStateAsync(cancellationToken);

        await Emit(new ExperienceListed(listing));

        // Dynamic marketplace surface refresh for gRPC clients (flutter OS shell etc). Newly listed/packed software (from auto pack on activate, peer sync, manual publish) now immediately appears in the "🛒 Marketplace" PinSurface view with install buttons etc. Initial surface at activate + this update ensures all core os software is visible/available to download without restart or re-pin. Rule still handles per-ExperienceListed declarative cards.
        var surf = ListingsSurface();
        SurfaceStreamService.Publish(SurfaceStreamService.ToMessage(surf));

        await Emit(new NeuronTelemetry(Self, "ExperienceListed", new Dictionary<string, string>
        {
            ["id"] = manifest.Id,
            ["hash"] = manifest.ContentHash,
            ["bytes"] = packageBytes.LongLength.ToString()
        }));

        // Basic OTel for distribution events (Aspire dashboard surfaces via tags on telemetry/ExperienceListed etc).
        if (Activity.Current is { } actListed)
        {
            actListed.SetTag("db.experience.id", manifest.Id);
            actListed.SetTag("db.distribution.event", "listed");
        }

        if (_state.State.GlobalPeer == null)
        {
            _state.State.GlobalPeer = new GlobalPeer(GlobalPeerAddressDefault, DateTimeOffset.UtcNow);
            await _state.WriteStateAsync(cancellationToken);
            await Emit(new NeuronTelemetry(Self, "GlobalPeerRegistered", new Dictionary<string, string> { ["address"] = GlobalPeerAddressDefault }));
            if (Activity.Current is { } actGlobal)
            {
                actGlobal.SetTag("db.global.peer", "registered");
            }
        }

        return listing;
    }

    public async Task<ExperienceListed> PublishLocalAsync(string experienceId, string? packagePath = null, CancellationToken cancellationToken = default)
    {
        var path = packagePath ?? FindLatestPackage(experienceId)
            ?? throw new InvalidOperationException($"No packed {ExperiencePackageFormat.Extension} found for '{experienceId}'. Pack it first (/pack {experienceId}).");

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var manifest = ReadManifest(bytes)
            ?? throw new InvalidOperationException($"Package at '{path}' has no readable {ExperiencePackageFormat.ManifestEntry}.");

        var listing = await AddListingAsync(manifest, bytes, cancellationToken);
        return new ExperienceListed(listing);
    }

    // E8 Discovery additions (minimal, reuses existing AddListing/peer machinery pattern).
    public Task AddPeerAsync(PeerInfo peer, CancellationToken cancellationToken = default)
    {
        _state.State.Peers[peer.World] = peer with { LastSeen = DateTimeOffset.UtcNow };
        return _state.WriteStateAsync(cancellationToken);
    }

    public Task<IReadOnlyList<PeerInfo>> ListPeersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PeerInfo>>(_state.State.Peers.Values.OrderByDescending(p => p.LastSeen).ToList());

    public async Task<ExperienceDownloaded> InstallListedAsync(string experienceId, CancellationToken cancellationToken = default)
    {
        var bytes = await GetPackageBytesAsync(experienceId, cancellationToken)
            ?? throw new InvalidOperationException($"'{experienceId}' is not listed on this marketplace.");
        return await VerifyExtractInstallAsync(experienceId, bytes, Domain, cancellationToken);
    }

    private async Task RunDistributionSimulationAsync(CancellationToken cancellationToken)
    {
        // Demo using real pa-files bundle. Publish lists it ("published" visible as new row in marketplace surface).
        // Install does the bundle activation (new handlers join, distribution N+1). Telemetry + surface refresh drive
        // TUI/Flutter feedback. Flutter side animates the resulting "installed" / published state cards.
        const string demoId = "awesome-se-team";

        if (!_state.State.Listings.ContainsKey(demoId))
        {
            await PublishLocalAsync(demoId, cancellationToken: cancellationToken);
            await Emit(new NeuronTelemetry(Self, "DistributionSimPublished", new Dictionary<string, string> { ["id"] = demoId }));
        }

        await InstallListedAsync(demoId, cancellationToken);
        await Emit(new NeuronTelemetry(Self, "DistributionSimInstalled", new Dictionary<string, string>
        {
            ["id"] = demoId,
            ["note"] = "new handlers now participate on timeline (distribution proof)"
        }));

        // Data-driven marketplace surface emitted from neuron state (rich listings); .ino rule supplements with declarative cards.
    }

    public async Task<ExperienceDownloaded> InstallFromPeerAsync(string peerAddress, string experienceId, CancellationToken cancellationToken = default)
    {
        byte[]? bytes;
        if (IsGlobalPeerAddress(peerAddress))
        {
            bytes = await GetGlobalPackageBytesAsync(experienceId, cancellationToken)
                ?? await GetPackageBytesAsync(experienceId, cancellationToken)
                ?? throw new InvalidOperationException($"'{experienceId}' is not listed on global peer.");
        }
        else
        {
            await using var peer = await MarketplacePeer.ConnectAsync(peerAddress, cancellationToken);
            var remote = MarketplacePeer.MarketplaceOf(peer);
            bytes = await remote.GetPackageBytesAsync(experienceId, cancellationToken)
                ?? throw new InvalidOperationException($"'{experienceId}' is not listed on peer '{peerAddress}'.");
        }

        return await VerifyExtractInstallAsync(experienceId, bytes, peerAddress, cancellationToken);
    }

    public async Task HandleAsync(PublishToMarketplace synapse, CancellationToken cancellationToken)
    {
        var listed = await PublishLocalAsync(synapse.ExperienceId, synapse.PackagePath, cancellationToken);

        if (string.IsNullOrWhiteSpace(synapse.PeerAddress)) return;

        var bytes = await GetPackageBytesAsync(synapse.ExperienceId, cancellationToken);
        if (bytes is null) return;

        await using var peer = await MarketplacePeer.ConnectAsync(synapse.PeerAddress, cancellationToken);
        await MarketplacePeer.MarketplaceOf(peer).AddListingAsync(listed.Listing.Manifest, bytes, cancellationToken);

        await Emit(new NeuronTelemetry(Self, "ExperiencePushedToPeer", new Dictionary<string, string>
        {
            ["id"] = synapse.ExperienceId,
            ["peer"] = synapse.PeerAddress
        }));

        // Auto federation push on publish (LAN kernel -> global peer appears as global listing for others to pull/install).
        if (_state.State.GlobalPeer != null && _state.State.GlobalPeer.Enabled)
        {
            await SyncListingsToGlobalAsync(synapse.ExperienceId, cancellationToken);
        }
    }

    private UiSurface ListingsSurface()
    {
        var ordered = _state.State.Listings.Values.OrderByDescending(l => l.PublishedAt).ToArray();
        var children = new List<UiWidget>();
        if (ordered.Length == 0)
        {
            children.Add(new Text("(no listings yet)"));
        }
        else
        {
            foreach (var l in ordered)
            {
                children.Add(new Text($"{l.Manifest.Id} v{l.Manifest.Version} • {l.SizeBytes} bytes • by {l.Manifest.Author}"));
                children.Add(new Button($"Install {l.Manifest.Id}", new InstallFromMarketplace((ExperienceId)l.Manifest.Id)));
            }
        }

        var globals = _state.State.GlobalListings.Values.OrderByDescending(l => l.PublishedAt).ToArray();
        if (globals.Length > 0)
        {
            children.Add(new Text("— Global / Community —"));
            foreach (var l in globals)
            {
                children.Add(new Text($"{l.Manifest.Id} v{l.Manifest.Version} (global) • {l.SizeBytes} bytes • by {l.Manifest.Author}"));
                var gaddr = _state.State.GlobalPeer?.Address ?? GlobalPeerAddressDefault;
                children.Add(new Button($"Install from global {l.Manifest.Id}", new InstallFromMarketplace((ExperienceId)l.Manifest.Id, gaddr)));
            }
        }

        if (_state.State.InstalledIds.Count > 0)
        {
            children.Add(new Text("— Installed —"));
            foreach (var installedId in _state.State.InstalledIds.OrderBy(id => id).ToArray())
            {
                children.Add(new Text(installedId));
                children.Add(new Button($"Uninstall {installedId}", new UninstallBundle(installedId)));
                // OS6: revoke grant button (emits GrantRevoked; handled in gmail/google-auth to clear allowedCapabilities; shown in Installed section)
                children.Add(new Button($"Revoke SaveFile for {installedId}", new GrantRevoked(installedId, new[] { "SaveFileRequest" })));
                if (_state.State.RuleCapableInstalledIds.Contains(installedId))
                {
                    children.Add(new Button($"▶ Run {installedId}", new RunExperience((ExperienceId)installedId)));
                }
            }
        }

        return new UiSurface("marketplace", Self, new Card($"🛒 Marketplace {Domain}", new Column(children.ToArray())));
    }

    public Task HandleAsync(InstallFromMarketplace synapse, CancellationToken cancellationToken) =>
        synapse.PeerAddress is null
            ? InstallListedAsync(synapse.ExperienceId, cancellationToken)
            : InstallFromPeerAsync(synapse.PeerAddress, synapse.ExperienceId, cancellationToken);

    public Task HandleAsync(RunDistributionSimulation synapse, CancellationToken cancellationToken) =>
        RunDistributionSimulationAsync(cancellationToken);

    public async Task HandleAsync(ListPublished synapse, CancellationToken cancellationToken) =>
        await PullPopularFromGlobalAsync(cancellationToken);

    private async Task<ExperienceDownloaded> VerifyExtractInstallAsync(string experienceId, byte[] packageBytes, string source, CancellationToken cancellationToken)
    {
        var manifest = ReadManifest(packageBytes)
            ?? throw new InvalidOperationException($"Package '{experienceId}' from '{source}' has no readable {ExperiencePackageFormat.ManifestEntry}.");

        // requires: check (OS3, D-OS5): emit actionable surface with install buttons for missing; no solver, user/ino orders the installs (seed list ensures)
        var brainRef = GrainFactory.GetGrain<IDigitalBrain>(Domain);
        if (manifest.Requires != null && manifest.Requires.Length > 0)
        {
            var curr = await brainRef.ListInstalledBundlesAsync(cancellationToken);
            var missing = manifest.Requires.Where(r => !curr.Any(c => string.Equals(c, r, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (missing.Length > 0)
            {
                var ch = new List<UiWidget> { new Text($"Requires: {string.Join(", ", missing)}") };
                foreach (var m in missing)
                    ch.Add(new Button($"Install {m}", new InstallFromMarketplace((ExperienceId)m)));
                await Emit(new UiSurface($"requires-{manifest.Id}", Self, new Card("Missing requirements", new Column(ch.ToArray()))));
                await Emit(new NeuronTelemetry(Self, "RequiresSurfaceEmitted", new Dictionary<string, string> { ["id"] = manifest.Id, ["missing"] = string.Join(",", missing) }));
                // Abort install until user/ino satisfies the requires via the surfaced buttons. No solver.
                // Return a non-success downloaded marker (callers check verified or presence).
                return new ExperienceDownloaded(manifest, string.Empty, false);
            }
        }

        // Dual support per OS-ON-YAML-SPEC: prefer yaml for os-on-yaml content, fallback to .ino for compat.
        var experienceContent = ReadEntry(packageBytes, ExperiencePackageFormat.YamlEntry)
            ?? ReadEntry(packageBytes, ExperiencePackageFormat.InoEntry);
        var contractJson = ReadEntry(packageBytes, ExperiencePackageFormat.ContractEntry);
        bool isContract = manifest.IsContractOnly;

        bool verified;
        if (isContract)
        {
            verified = contractJson is not null &&
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(contractJson))) == manifest.ContentHash;
        }
        else
        {
            verified = experienceContent is not null &&
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(experienceContent))) == manifest.ContentHash;
        }

        // alwaysVerifySignatureOrQuarantine: require sig for full verified; unsigned or bad -> !verified + quarantine per E vision.
        if (verified)
        {
            if (!string.IsNullOrWhiteSpace(manifest.SignatureBase64) && !string.IsNullOrWhiteSpace(manifest.AuthorPublicKeyBase64))
            {
                verified = VerifyManifestSignature(manifest, manifest.ContentHash);
            }
            else
            {
                verified = false; // unsigned (no sig fields) routes through quarantine gate (strengthen trust; legacy compat noted in DELETED)
            }
        }

        var downloadDirectory = Path.Combine("pa-files", "downloads", Sanitize(Domain), Sanitize(experienceId));
        Directory.CreateDirectory(downloadDirectory);
        var packagePath = Path.Combine(downloadDirectory, $"{Sanitize(experienceId)}{ExperiencePackageFormat.Extension}");
        await File.WriteAllBytesAsync(packagePath, packageBytes, cancellationToken);

        string? contentPath = null;
        if (!isContract && experienceContent is not null)
        {
            var entryName = packageBytes is not null && ReadEntry(packageBytes, ExperiencePackageFormat.YamlEntry) is not null
                ? ExperiencePackageFormat.YamlEntry
                : ExperiencePackageFormat.InoEntry;
            contentPath = Path.Combine(downloadDirectory, entryName);
            await File.WriteAllTextAsync(contentPath, experienceContent, cancellationToken);
        }

        var downloaded = new ExperienceDownloaded(manifest, packagePath, verified);
        await Emit(downloaded);

        if (!verified)
        {
            await Emit(new NeuronTelemetry(Self, "ExperienceHashMismatch", new Dictionary<string, string>
            {
                ["id"] = experienceId,
                ["source"] = source,
                ["expected"] = manifest.ContentHash
            }));
            await Emit(new StartQuarantineWorld((ExperienceId)experienceId, source));
            return downloaded;
        }

        var brain = GrainFactory.GetGrain<IDigitalBrain>(Domain);
        await brain.InstallBundleAsync(new InstallBundle((BundleId)manifest.Id, contentPath, IsContractOnly: isContract, ContractHandlers: manifest.ContractHandlers, HasRules: manifest.HasRules), cancellationToken);

        if (!_state.State.InstalledIds.Contains(manifest.Id))
            _state.State.InstalledIds.Add(manifest.Id);
        if (manifest.HasRules && !_state.State.RuleCapableInstalledIds.Contains(manifest.Id))
            _state.State.RuleCapableInstalledIds.Add(manifest.Id);
        await _state.WriteStateAsync(cancellationToken);

        // Refresh the marketplace surface for gRPC clients (e.g. flutter shell) after a successful install from marketplace.
        // This makes the Installed section + Run buttons appear immediately in the open "🛒 Marketplace" view (the ListingsSurface reflects the updated InstalledIds/RuleCapable lists).
        // Complements the earlier AddListing refresh for new listings; keeps the tap -> install -> UI update loop working end-to-end without re-pinning.
        var marketplaceSurface = ListingsSurface();
        SurfaceStreamService.Publish(SurfaceStreamService.ToMessage(marketplaceSurface));

        await Emit(new NeuronTelemetry(Self, "ExperienceInstalledFromMarketplace", new Dictionary<string, string>
        {
            ["id"] = experienceId,
            ["source"] = source,
            ["hash"] = manifest.ContentHash,
            ["isContract"] = isContract.ToString()
        }));

        // OS6: privileged (google/gmail id or SaveFileRequest) emit GrantRequested (amends Q2; stored per-bundle+cap in brain; enforced at install + RuleHost).
        // Surface with buttons emits GrantDecision (tolerant stubs in sim; real in kernel).
        if (experienceId.Contains("google", StringComparison.OrdinalIgnoreCase) || experienceId.Contains("gmail", StringComparison.OrdinalIgnoreCase))
        {
            await Emit(new GrantRequested(experienceId, new[] { "SaveFileRequest", "GoogleApi" }));
            // compat for U4
            await Emit(new CapabilityGrantRequest(experienceId, new[] { "SaveFileRequest", "GoogleApi" }));
        }

        return downloaded;
    }

    private static string? FindLatestPackage(string experienceId)
    {
        var directory = Path.Combine("pa-files", "packages");
        if (!Directory.Exists(directory)) return null;
        var sanitized = Sanitize(experienceId);
        return Directory.EnumerateFiles(directory, $"{sanitized}-*{ExperiencePackageFormat.Extension}")
            .Concat(Directory.EnumerateFiles(directory, $"{sanitized}{ExperiencePackageFormat.Extension}"))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static ExperienceManifest? ReadManifest(byte[] packageBytes)
    {
        var json = ReadEntry(packageBytes, ExperiencePackageFormat.ManifestEntry);
        return json is null ? null : JsonSerializer.Deserialize<ExperienceManifest>(json, Json);
    }

    private static string? ReadEntry(byte[] packageBytes, string entryName)
    {
        using var stream = new MemoryStream(packageBytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.GetEntry(entryName);
        if (entry is null) return null;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Sanitize(string value) =>
        new(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());

    private bool VerifyManifestSignature(ExperienceManifest manifest, string contentHash)
    {
        if (string.IsNullOrWhiteSpace(manifest.AuthorPublicKeyBase64) || string.IsNullOrWhiteSpace(manifest.SignatureBase64)) return false;
        try
        {
            var pub = new Ed25519PublicKeyParameters(Convert.FromBase64String(manifest.AuthorPublicKeyBase64));
            var sig = Convert.FromBase64String(manifest.SignatureBase64);
            var data = Encoding.UTF8.GetBytes(manifest.Id + "|" + manifest.Version + "|" + contentHash + "|" + manifest.AuthorPublicKeyBase64);
            var v = new Ed25519Signer();
            v.Init(false, pub);
            v.BlockUpdate(data, 0, data.Length);
            return v.VerifySignature(sig);
        }
        catch { return false; }
    }

    public async Task HandleAsync(UpdateBundle synapse, CancellationToken cancellationToken)
    {
        await Emit(new NeuronTelemetry(Self, "UpdateRequested", new Dictionary<string, string> { ["id"] = synapse.ExperienceId.Value }));
        if (synapse.PeerAddress is null)
            await InstallListedAsync(synapse.ExperienceId, cancellationToken);
        else
            await InstallFromPeerAsync(synapse.PeerAddress, synapse.ExperienceId, cancellationToken);
    }

    public async Task HandleAsync(StartQuarantineWorld synapse, CancellationToken cancellationToken)
    {
        await Emit(new StartQuarantineWorld(synapse.ExperienceId, synapse.PeerAddress));
    }

    public async Task SyncListingsToGlobalAsync(string experienceId, CancellationToken cancellationToken = default)
    {
        if (_state.State.GlobalPeer == null || !_state.State.GlobalPeer.Enabled) return;

        var bytes = await GetPackageBytesAsync(experienceId, cancellationToken);
        if (bytes is null) return;

        var manifest = _state.State.Listings.TryGetValue(experienceId, out var lst) ? lst.Manifest : ReadManifest(bytes);
        if (manifest is null) return;

        var addr = _state.State.GlobalPeer.Address;
        bool remote = false;
        try
        {
            await using var peer = await MarketplacePeer.ConnectAsync(addr, cancellationToken);
            var remoteMkt = MarketplacePeer.MarketplaceOf(peer);
            await remoteMkt.AddListingAsync(manifest, bytes, cancellationToken);
            remote = true;
        }
        catch
        {
            _state.State.GlobalListings[experienceId] = _state.State.Listings.TryGetValue(experienceId, out var l) ? l : new ExperienceListing(manifest, bytes.LongLength, DateTimeOffset.UtcNow);
            var gpath = Path.Combine("pa-files", "marketplace", "global", $"{Sanitize(experienceId)}{ExperiencePackageFormat.Extension}");
            Directory.CreateDirectory(Path.GetDirectoryName(gpath)!);
            await File.WriteAllBytesAsync(gpath, bytes, cancellationToken);
            _state.State.GlobalPackagePaths[experienceId] = gpath;
            await _state.WriteStateAsync(cancellationToken);
        }

        await Emit(new GlobalListingsSynced(new[] { experienceId }, DateTimeOffset.UtcNow));
        await Emit(new NeuronTelemetry(Self, "GlobalListingsSynced", new Dictionary<string, string>
        {
            ["id"] = experienceId,
            ["addr"] = addr,
            ["remote"] = remote.ToString()
        }));
        if (Activity.Current is { } act) act.SetTag("db.global.sync", experienceId);
    }

    public async Task PullPopularFromGlobalAsync(CancellationToken cancellationToken = default)
    {
        var addr = _state.State.GlobalPeer?.Address ?? GlobalPeerAddressDefault;
        IReadOnlyList<ExperienceListing> received;
        bool remote = false;
        try
        {
            await using var peer = await MarketplacePeer.ConnectAsync(addr, cancellationToken);
            var remoteMkt = MarketplacePeer.MarketplaceOf(peer);
            received = await remoteMkt.ListAsync(cancellationToken);
            remote = true;
            foreach (var l in received.Take(5))
            {
                _state.State.GlobalListings[l.Manifest.Id] = l;
            }
            await _state.WriteStateAsync(cancellationToken);
        }
        catch
        {
            received = _state.State.GlobalListings.Values.ToArray();
        }

        var ids = received.Select(l => l.Manifest.Id).ToArray();
        await Emit(new GlobalListingsReceived(ids));
        await Emit(new NeuronTelemetry(Self, "GlobalListingsReceived", new Dictionary<string, string>
        {
            ["count"] = ids.Length.ToString(),
            ["addr"] = addr,
            ["remote"] = remote.ToString()
        }));
    }

    public async Task<ExperienceRated> RateExperienceAsync(string experienceId, int rating, string? comment = null, CancellationToken cancellationToken = default)
    {
        var at = DateTimeOffset.UtcNow;
        var r = new ExperienceRating(experienceId, rating, comment, at);
        var arr = _state.State.Ratings.TryGetValue(experienceId, out var ex) ? ex : Array.Empty<ExperienceRating>();
        _state.State.Ratings[experienceId] = arr.Append(r).ToArray();
        await _state.WriteStateAsync(cancellationToken);

        var rated = new ExperienceRated(experienceId, rating, comment, at);
        await Emit(rated);
        await Emit(new NeuronTelemetry(Self, "ExperienceRated", new Dictionary<string, string>
        {
            ["id"] = experienceId,
            ["rating"] = rating.ToString(),
            ["global"] = IsGlobalPeerAddress(_state.State.GlobalPeer?.Address).ToString()
        }));

        if (rating >= 4)
        {
            await Emit(new NeuronTelemetry(Self, "CommunityEndorsed", new Dictionary<string, string> { ["id"] = experienceId }));
        }
        return rated;
    }

    public async Task HandleAsync(SyncListingsToGlobal synapse, CancellationToken cancellationToken) =>
        await SyncListingsToGlobalAsync(synapse.ExperienceId, cancellationToken);

    public async Task HandleAsync(PullPopularFromGlobal synapse, CancellationToken cancellationToken) =>
        await PullPopularFromGlobalAsync(cancellationToken);

    public async Task HandleAsync(RateExperience synapse, CancellationToken cancellationToken) =>
        await RateExperienceAsync(synapse.ExperienceId, synapse.Rating, synapse.Comment, cancellationToken);

    public Task HandleAsync(BundleUninstalled synapse, CancellationToken cancellationToken)
    {
        if (_state.State.InstalledIds.Remove(synapse.BundleId))
        {
            _state.State.RuleCapableInstalledIds.Remove(synapse.BundleId);
            _ = _state.WriteStateAsync(cancellationToken);
            // Refreshed listings surface emit removed (rule path in marketplace.ino is single source for ExperienceListed cards).
            // ListingsSurface() retained for List API consumers if needed.
        }
        return Task.CompletedTask;
    }

    // Task2: ensure real .brain packing completeness for every os experience (os-on-yaml/*.yaml preferred per SPEC for new schema paradigm / neurons+synapses; fallback os/*.ino for dual compat).
    // Uses packAndPublishIfMissing (content from os-on-yaml/ or os/ for faithful source; pack always signs via brain Ed25519 identity per Packager).
    // Ref: BouncyCastle Ed25519 already in use for sign/verify (Context7/web prior: Ed25519Signer init(true/false) + Generate/VerifySignature); Orleans direct reliable; no new pkgs.
    // Unification note (Y8 start per OS-ON-YAML-PLAN): yaml now canonical source for os definition; .ino kept for compat until owner + gates approve full cutover.
    private async Task EnsureAllOsInoBundlesPackedAsync(CancellationToken cancellationToken)
    {
        var osInoIdsToEnsurePacked = new[]
        {
            "awesome-se-team", "creator", "example-world", "gmail-last-senders", "gmail-senders-chart", "google-auth", "hex-guide",
            "kernel-tasks", "llm-agent", "marketplace", "memory", "packager", "shell", "transcription", "weather-watcher"
        };
        foreach (var id in osInoIdsToEnsurePacked)
        {
            await packAndPublishIfMissing(id, cancellationToken);
        }
    }

    private async Task packAndPublishIfMissing(string id, CancellationToken cancellationToken)
    {
        if (_state.State.Listings.ContainsKey(id)) return;
        // pre-seed .brain in pa-files/marketplace compat (e.g. google/llm/awesome)
        var prePath = Path.Combine("pa-files", "marketplace", $"{Sanitize(id)}.brain");
        if (File.Exists(prePath))
        {
            await SeedL1CapsuleIfMissing(id, cancellationToken);
            return;
        }
        // auto from real os-on-yaml/{id}.yaml (preferred for new schema paradigm per SPEC) or fallback os/{id}.ino (faithful source, not derived; dual pack path)
        string? content = null;
        string sourceDesc = $"os/{id}";
        var osYamlPath = Path.Combine("os-on-yaml", $"{Sanitize(id)}.yaml");
        if (File.Exists(osYamlPath))
        {
            content = await File.ReadAllTextAsync(osYamlPath, cancellationToken);
            sourceDesc = $"os-on-yaml/{id}";
        }
        else
        {
            var osPath = Path.Combine("os", $"{Sanitize(id)}.ino");
            if (File.Exists(osPath))
            {
                content = await File.ReadAllTextAsync(osPath, cancellationToken);
            }
        }
        var packager = GrainFactory.GetGrain<IPackager>(Brain.WellKnownKey);
        var packed = await packager.PackAsync(id, $"{sourceDesc} auto for marketplace completeness + trust", "0.1.0", content, false, null, cancellationToken);
        await PublishLocalAsync(id, packed.PackagePath, cancellationToken);
        await Emit(new NeuronTelemetry(Self, "OsInoAutoPackedForTrust", new Dictionary<string, string> { ["id"] = id }));
    }
}
