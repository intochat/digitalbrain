using System.IO.Compression;
using System.Text.Json;
using DigitalBrain.Runtime.Marketplace;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.InoLang;
using DigitalBrain.InoLang.Linking;
using Orleans.Journaling;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime;
using DigitalBrain.SDK.Stripe;

namespace DigitalBrain.Kernel.Runtime.Neurons;

/// <summary>
/// The Marketplace SaaS Neuron. Orchestrates bundle upload validation, cryptographic signing, purchasing, and billing payouts.
/// </summary>
[GrainType("DigitalBrain.Kernel.Runtime.Neurons.MarketplaceNeuron")]
[ImplicitStreamSubscription(nameof(MarketplaceNeuron))]
public sealed class MarketplaceNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    [FromKeyedServices("marketplace-keypair")] IDurableList<byte[]> keyPairBytes,
    IContractCatalog contractCatalog,
    IStripeGateway stripe,
    IGrainFactory grains,
    IServiceProvider serviceProvider,
    ILogger<MarketplaceNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      IMarketplaceNeuron,
      INeuronMetadata,
      IHandle<GetBundlesQuery>,
      IHandle<PublishBundleCommand>,
      IHandle<BuyBundleCommand>,
      IHandle<ConfirmCheckoutCommand>,
      IHandle<InstallMarketplaceNeuronCommand>
{
    public static NeuronId         Id           => new("kernel/marketplace");
    public static string           Icon         => "shopping_bag";
    public static NeuronCapability Capabilities => NeuronCapability.External | NeuronCapability.Storage;

    private byte[]? _privateKey;
    private byte[]? _publicKey;

    private async Task EnsureKeysAsync()
    {
        if (_privateKey is not null && _publicKey is not null) return;

        if (keyPairBytes.Count >= 2)
        {
            _privateKey = keyPairBytes[0];
            _publicKey = keyPairBytes[1];
            return;
        }

        var (priv, pub) = BundleSignatureVerifier.GenerateKeyPair();
        keyPairBytes.Add(priv);
        keyPairBytes.Add(pub);
        await WriteStateAsync();

        _privateKey = priv;
        _publicKey = pub;
        Logger.LogInformation("Generated new persistent ECDSA key pair for MarketplaceNeuron.");
    }

    public async Task<List<BundleInfo>> GetCatalogAsync()
    {
        var db = Grains.GetGrain<IPostgresDbNeuron>("marketplace-db");
        return await db.SelectBundlesAsync();
    }

    public async Task<byte[]> GetPublicKeyAsync()
    {
        await EnsureKeysAsync();
        return _publicKey!;
    }

    private sealed class BundleManifestNeuron
    {
        public string? Fqn { get; set; }
        public string? SourcePath { get; set; }
    }

    private sealed class BundleManifest
    {
        public string? BundleId { get; set; }
        public string? Version { get; set; }
        public List<BundleManifestNeuron>? Neurons { get; set; }
    }

    public async Task<PublishBundleResponse> PublishBundleAsync(
        string bundleId,
        string version,
        string manifestJson,
        byte[] zipBytes)
    {
        var diagnostics = new List<string>();
        Logger.LogInformation("Marketplace: starting publication process for bundle: {BundleId} ({Version})", bundleId, version);

        try
        {
            await EnsureKeysAsync();

            // 1. Unpack ZIP and validate files
            using var zipStream = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry is null)
            {
                diagnostics.Add("Publish failure: missing 'manifest.json' in bundle archive.");
                return CreatePublishResponse(false, diagnostics);
            }

            using var manifestStream = manifestEntry.Open();
            var manifest = await JsonSerializer.DeserializeAsync<BundleManifest>(
                manifestStream,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            if (manifest?.BundleId != bundleId || manifest.Version != version || manifest.Neurons is null)
            {
                diagnostics.Add("Publish failure: malformed or mismatched 'manifest.json' in bundle archive.");
                return CreatePublishResponse(false, diagnostics);
            }

            var scannedPrice = "free";
            var scannedLicense = "source-included";

            // 2. Local InoLang Compilation & Metadata Scan for every Neuron
            foreach (var neuron in manifest.Neurons)
            {
                if (string.IsNullOrWhiteSpace(neuron.Fqn) || string.IsNullOrWhiteSpace(neuron.SourcePath))
                {
                    diagnostics.Add("Publish warning: invalid neuron declaration found in manifest, skipping.");
                    continue;
                }

                var cleanPath = neuron.SourcePath.Replace('\\', '/');
                var sourceEntry = archive.GetEntry(cleanPath);
                if (sourceEntry is null)
                {
                    diagnostics.Add($"Publish failure: neuron source path '{neuron.SourcePath}' not found in ZIP.");
                    return CreatePublishResponse(false, diagnostics);
                }

                using var sourceStream = sourceEntry.Open();
                using var reader = new StreamReader(sourceStream);
                var source = await reader.ReadToEndAsync();

                // Call local InoCompiler to verify compilation
                var compiled = InoCompiler.Compile(source, contractCatalog);
                if (!compiled.Success)
                {
                    diagnostics.Add($"Publish failure: InoLang compilation failed for '{neuron.Fqn}': {string.Join(";", compiled.Diagnostics.Select(d => d.Message))}");
                    return CreatePublishResponse(false, diagnostics);
                }

                // Scan metadata tags
                var meta = InoMetadataScanner.Scan(source);
                if (!string.Equals(meta.Price, "free", StringComparison.OrdinalIgnoreCase))
                {
                    scannedPrice = meta.Price;
                }
                if (!string.Equals(meta.License, "source-included", StringComparison.OrdinalIgnoreCase))
                {
                    scannedLicense = meta.License;
                }
            }

            // 3. Cryptographically sign the manifest
            var signature = BundleSignatureVerifier.SignData(manifestJson, _privateKey!);

            // 4. Save to Database Neuron
            var db = Grains.GetGrain<IPostgresDbNeuron>("marketplace-db");
            var bundleRecord = new BundleInfo(bundleId, version, manifestJson, signature, scannedPrice, scannedLicense, zipBytes);
            await db.InsertBundleAsync(bundleRecord);

            diagnostics.Add($"Successfully compiled, cryptographically signed, and published bundle '{bundleId}' v{version}");
            Logger.LogInformation("Marketplace: successfully published bundle '{BundleId}' (Price: {Price}, License: {License})", bundleId, scannedPrice, scannedLicense);
            return CreatePublishResponse(true, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Publish fatal exception: {ex.Message}");
            Logger.LogError(ex, "Marketplace: publication failed with exception.");
            return CreatePublishResponse(false, diagnostics);
        }
    }

    public async Task<BuyBundleResponse> BuyBundleAsync(string bundleId, string userId)
    {
        try
        {
            Logger.LogInformation("Marketplace: processing buy order for bundle '{BundleId}' by user '{UserId}'", bundleId, userId);

            var db = Grains.GetGrain<IPostgresDbNeuron>("marketplace-db");
            var catalog = await db.SelectBundlesAsync();
            var bundle = catalog.FirstOrDefault(b => b.BundleId == bundleId);
            if (bundle is null)
            {
                return CreateBuyResponse(false, string.Empty, $"Bundle '{bundleId}' not found in catalog.");
            }

            var isPremium = !string.Equals(bundle.Price, "free", StringComparison.OrdinalIgnoreCase);

            // Free bundles carry no payment, so the entitlement is granted immediately — the
            // same fast-path the git-clone domain install uses, generalized to a zip.
            if (!isPremium)
            {
                var freeLicense = await FulfillAsync(bundleId, userId, $"free_{Guid.NewGuid():N}");
                return CreateBuyResponse(true, freeLicense, string.Empty);
            }

            // Premium: open a Stripe Checkout session and hand the buyer its URL. No purchase
            // row, no license — those are written only when the webhook confirms payment
            // (see ConfirmCheckoutAsync). Entitlement never precedes payment.
            var checkout = await stripe.CreateCheckoutSessionAsync(new StripeCheckoutRequest(
                BundleId: bundleId,
                UserId: userId,
                Price: bundle.Price,
                ProductName: bundleId,
                SuccessUrl: "https://digitalbrain.tech/marketplace/success?session_id={CHECKOUT_SESSION_ID}",
                CancelUrl: "https://digitalbrain.tech/marketplace/cancel"));

            Logger.LogInformation("Marketplace: opened checkout session '{SessionId}' for bundle '{BundleId}'; awaiting payment confirmation.", checkout.SessionId, bundleId);
            return CreateBuyResponse(true, string.Empty, string.Empty, checkout.Url, checkout.SessionId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Marketplace: buy request encountered an error.");
            return CreateBuyResponse(false, string.Empty, $"Purchase failed: {ex.Message}");
        }
    }

    public async Task<ConfirmCheckoutResponse> ConfirmCheckoutAsync(string stripeEventJson, string stripeSignature)
    {
        try
        {
            // Verify the Stripe event through the connector before trusting any of it.
            var evt = stripe.VerifyEvent(stripeEventJson, stripeSignature);
            if (!evt.Ok)
            {
                Logger.LogWarning("Marketplace: rejecting unverified Stripe webhook ({Reason}); no license issued.", evt.Reason);
                return CreateConfirmResponse(false, string.Empty, $"Webhook rejected: {evt.Reason}");
            }

            if (!string.Equals(evt.EventType, "checkout.session.completed", StringComparison.Ordinal))
            {
                Logger.LogInformation("Marketplace: ignoring Stripe event '{EventType}' — not a completed checkout; no license issued.", evt.EventType);
                return CreateConfirmResponse(false, string.Empty, $"Ignored event '{evt.EventType}'.");
            }

            if (string.IsNullOrEmpty(evt.BundleId) || string.IsNullOrEmpty(evt.UserId))
            {
                Logger.LogWarning("Marketplace: completed checkout '{SessionId}' is missing bundle/user metadata; no license issued.", evt.SessionId);
                return CreateConfirmResponse(false, string.Empty, "Completed checkout missing bundle/user metadata.");
            }

            var licenseToken = await FulfillAsync(evt.BundleId, evt.UserId, evt.SessionId ?? evt.EventId);
            return CreateConfirmResponse(true, licenseToken, $"Payment confirmed for '{evt.BundleId}'.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Marketplace: confirm-checkout encountered an error.");
            return CreateConfirmResponse(false, string.Empty, $"Confirmation failed: {ex.Message}");
        }
    }

    // Records the purchase and issues the license. The single place entitlement is granted,
    // reached only once payment is confirmed (or the bundle is free). Idempotent: a repeat
    // webhook for the same (user, bundle) returns the existing token rather than re-issuing.
    private async Task<string> FulfillAsync(string bundleId, string userId, string transactionRef)
    {
        var db = Grains.GetGrain<IPostgresDbNeuron>("marketplace-db");

        var existing = await db.SelectLicensesAsync(userId, bundleId);
        var existingToken = existing.OrderByDescending(l => l.IssuedAtUtc).FirstOrDefault()?.LicenseToken;
        if (!string.IsNullOrEmpty(existingToken))
        {
            Logger.LogInformation("Marketplace: '{User}' already holds a license for '{Bundle}'; skipping re-issue.", userId, bundleId);
            return existingToken;
        }

        await db.InsertPurchaseAsync(new PurchaseRow($"txn_{transactionRef}", bundleId, userId, DateTimeOffset.UtcNow));

        var licenseServer = Grains.GetGrain<ILicenseNeuron>(BrainScopeHelper.GetActiveScopedNeuronKey("license-server"));
        var licenseToken = await licenseServer.IssueLicenseAsync(bundleId, userId);

        Logger.LogInformation("Marketplace: fulfilled '{Bundle}' for '{User}' (txn {Txn}); license issued.", bundleId, userId, transactionRef);
        return licenseToken;
    }

    public async Task<byte[]> DownloadBundleAsync(string bundleId, string userId)
    {
        var db = Grains.GetGrain<IPostgresDbNeuron>("marketplace-db");
        var catalog = await db.SelectBundlesAsync();
        var bundle = catalog.FirstOrDefault(b => b.BundleId == bundleId);

        if (bundle?.ZipBytes is null || bundle.ZipBytes.Length == 0)
        {
            Logger.LogWarning("Marketplace: download requested for unavailable bundle content '{BundleId}'.", bundleId);
            return Array.Empty<byte>();
        }

        var isPremium = !string.Equals(bundle.Price, "free", StringComparison.OrdinalIgnoreCase);
        if (isPremium)
        {
            var licenses = await db.SelectLicensesAsync(userId, bundleId);
            if (licenses.Count == 0)
            {
                Logger.LogWarning("Marketplace: download denied for premium bundle '{BundleId}' — user '{UserId}' holds no license.", bundleId, userId);
                return Array.Empty<byte>();
            }
        }

        Logger.LogInformation("Marketplace: serving {Bytes} bytes for bundle '{BundleId}' to user '{UserId}'.", bundle.ZipBytes.Length, bundleId, userId);
        return bundle.ZipBytes;
    }

    public async Task<InstallMarketplaceNeuronResponse> InstallMarketplaceNeuronAsync(string bundleId, string userId)
    {
        var diagnostics = new List<string>();
        Logger.LogInformation("Marketplace: starting installation process for bundle: {BundleId} for user {UserId}", bundleId, userId);

        try
        {
            // 1. Download the purchased bundle content (entitlement-gated).
            var zipBytes = await DownloadBundleAsync(bundleId, userId);
            if (zipBytes.Length == 0)
            {
                diagnostics.Add($"Installation failed: bundle '{bundleId}' is unavailable or not entitled for user '{userId}'.");
                return CreateInstallResponse(false, diagnostics);
            }

            // 2. Resolve the publisher signature + (premium only) the buyer's license token.
            var db = Grains.GetGrain<IPostgresDbNeuron>("marketplace-db");
            var bundle = (await db.SelectBundlesAsync()).FirstOrDefault(b => b.BundleId == bundleId);
            var isPremium = bundle is not null && !string.Equals(bundle.Price, "free", StringComparison.OrdinalIgnoreCase);

            string? licenseToken = null;
            if (isPremium)
            {
                var licenses = await db.SelectLicensesAsync(userId, bundleId);
                licenseToken = licenses.OrderByDescending(l => l.IssuedAtUtc).FirstOrDefault()?.LicenseToken;
                if (string.IsNullOrEmpty(licenseToken))
                {
                    diagnostics.Add($"Installation failed: no license on file for premium bundle '{bundleId}'.");
                    return CreateInstallResponse(false, diagnostics);
                }
            }

            // 3. Re-attach the marketplace signature + buyer license as separate zip
            //    entries (Option A), leaving the publisher-signed manifest.json byte-identical.
            var installableBytes = RepackForInstall(zipBytes, bundle?.Signature, licenseToken);

            // 4. Hand the prepared bundle to the real installer: it verifies the signature
            //    + entitlement, compiles each .ino, and registers the neurons live.
            var tempPath = Path.Combine(Path.GetTempPath(), $"dbom-{Guid.NewGuid():N}.bdom");
            await File.WriteAllBytesAsync(tempPath, installableBytes);
            try
            {
                var installer = serviceProvider.GetRequiredService<IBundleInstaller>();
                var result = await installer.InstallLocalAsync(tempPath, CancellationToken.None);
                diagnostics.AddRange(result.Diagnostics);
                if (result.Success)
                {
                    diagnostics.Add($"Installed bundle '{bundleId}'; registered: {string.Join(", ", result.RegisteredNeuronFqns)}.");
                }
                return CreateInstallResponse(result.Success, diagnostics);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { /* best-effort temp cleanup */ }
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Installation failed with fatal exception: {ex.Message}");
            Logger.LogError(ex, "Marketplace: installation failed with exception.");
            return CreateInstallResponse(false, diagnostics);
        }
    }

    // Re-attaches the marketplace's publisher signature and the buyer's license as
    // separate zip entries (signature.dat / license.dat). The signed manifest.json is
    // never modified, so the publisher signature still verifies over its exact bytes.
    private static byte[] RepackForInstall(byte[] zipBytes, byte[]? signature, string? licenseToken)
    {
        var output = new MemoryStream();
        output.Write(zipBytes, 0, zipBytes.Length);
        output.Position = 0;

        using (var archive = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            if (signature is { Length: > 0 })
            {
                archive.GetEntry("signature.dat")?.Delete();
                using var writer = new StreamWriter(archive.CreateEntry("signature.dat").Open());
                writer.Write(Convert.ToBase64String(signature));
            }

            if (!string.IsNullOrEmpty(licenseToken))
            {
                archive.GetEntry("license.dat")?.Delete();
                using var writer = new StreamWriter(archive.CreateEntry("license.dat").Open());
                writer.Write(licenseToken);
            }
        }

        return output.ToArray();
    }

    private PublishBundleResponse CreatePublishResponse(bool success, List<string> diagnostics)
    {
        return new PublishBundleResponse(Success: success,
        Diagnostics: diagnostics) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: Guid.Empty,
            causationId: null,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(MarketplaceNeuron),
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: string.Empty,
            timestamp: DateTimeOffset.UtcNow
        ) };
    }

    private BuyBundleResponse CreateBuyResponse(bool success, string licenseToken, string error, string checkoutUrl = "", string checkoutSessionId = "")
    {
        return new BuyBundleResponse(Success: success,
        LicenseToken: licenseToken,
        ErrorMessage: error,
        CheckoutUrl: checkoutUrl,
        CheckoutSessionId: checkoutSessionId) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: Guid.Empty,
            causationId: null,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(MarketplaceNeuron),
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: string.Empty,
            timestamp: DateTimeOffset.UtcNow
        ) };
    }

    private ConfirmCheckoutResponse CreateConfirmResponse(bool success, string licenseToken, string message)
    {
        return new ConfirmCheckoutResponse(Success: success,
        LicenseToken: licenseToken,
        Message: message) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: Guid.Empty,
            causationId: null,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(MarketplaceNeuron),
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: string.Empty,
            timestamp: DateTimeOffset.UtcNow
        ) };
    }

    private InstallMarketplaceNeuronResponse CreateInstallResponse(bool success, List<string> diagnostics)
    {
        return new InstallMarketplaceNeuronResponse(Success: success,
        Diagnostics: diagnostics) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: Guid.Empty,
            causationId: null,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(MarketplaceNeuron),
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: string.Empty,
            timestamp: DateTimeOffset.UtcNow
        ) };
    }

    // ==========================================
    // SYNAPSE STREAM SIGNAL HANDLERS
    // ==========================================

    protected override async Task HandleSynapseAsync(Synapse s)
    {
        switch (s)
        {
            case GetBundlesQuery query:
                var catalog = await GetCatalogAsync();
                await FireSynapseAsync(new GetBundlesResponse(Bundles: catalog) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: query.CorrelationId,
            causationId: query.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(MarketplaceNeuron),
            receiverNeuronId: query.CallerNeuronId,
            receiverNeuronType: query.CallerNeuronType ?? string.Empty,
            timestamp: DateTimeOffset.UtcNow
        ) });
                break;

            case PublishBundleCommand publish:
                var pubRes = await PublishBundleAsync(publish.BundleId, publish.Version, publish.ManifestJson, publish.ZipBytes);
                await FireSynapseAsync(pubRes with
                {
                    CorrelationId = publish.CorrelationId,
                    CausationId = publish.SynapseId,
                    ReceiverNeuronId = publish.CallerNeuronId,
                    ReceiverNeuronType = publish.CallerNeuronType ?? string.Empty
                });
                break;

            case BuyBundleCommand buy:
                var buyRes = await BuyBundleAsync(buy.BundleId, buy.UserId);
                await FireSynapseAsync(buyRes with
                {
                    CorrelationId = buy.CorrelationId,
                    CausationId = buy.SynapseId,
                    ReceiverNeuronId = buy.CallerNeuronId,
                    ReceiverNeuronType = buy.CallerNeuronType ?? string.Empty
                });
                break;

            case ConfirmCheckoutCommand confirm:
                var confirmRes = await ConfirmCheckoutAsync(confirm.StripeEventJson, confirm.StripeSignature);
                await FireSynapseAsync(confirmRes with
                {
                    CorrelationId = confirm.CorrelationId,
                    CausationId = confirm.SynapseId,
                    ReceiverNeuronId = confirm.CallerNeuronId,
                    ReceiverNeuronType = confirm.CallerNeuronType ?? string.Empty
                });
                break;

            case InstallMarketplaceNeuronCommand install:
                var installRes = await InstallMarketplaceNeuronAsync(install.BundleId, install.UserId);
                await FireSynapseAsync(installRes with
                {
                    CorrelationId = install.CorrelationId,
                    CausationId = install.SynapseId,
                    ReceiverNeuronId = install.CallerNeuronId,
                    ReceiverNeuronType = install.CallerNeuronType ?? string.Empty
                });
                break;
        }
    }
}
