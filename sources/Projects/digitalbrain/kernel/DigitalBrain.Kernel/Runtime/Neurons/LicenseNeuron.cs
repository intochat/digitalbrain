using System.Text;
using System.Text.Json;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Marketplace;
using DigitalBrain.Runtime.Neurons;
using Orleans.Journaling;

namespace DigitalBrain.Kernel.Runtime.Neurons;

/// <summary>
/// The Licensing Server Neuron. Generates ECDSA credentials, issues signed license tokens, and verifies entitlements against PostgresDbNeuron.
/// </summary>
[GrainType("DigitalBrain.Kernel.Runtime.Neurons.LicenseNeuron")]
[ImplicitStreamSubscription(nameof(LicenseNeuron))]
public sealed class LicenseNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    [FromKeyedServices("license-keypair")] IDurableList<byte[]> keyPairBytes,
    IGrainFactory grains,
    ILogger<LicenseNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      ILicenseNeuron,
      INeuronMetadata,
      IHandle<IssueLicenseCommand>,
      IHandle<VerifyLicenseQuery>
{
    public static NeuronId         Id           => new("kernel/licensing");
    public static string           Icon         => "key";
    public static NeuronCapability Capabilities => NeuronCapability.Storage | NeuronCapability.Reasoning;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        VerifyLicenseCommandLineArgs();
    }

    public Task CheckLicenseAgreementAsync()
    {
        VerifyLicenseCommandLineArgs();
        return Task.CompletedTask;
    }

    private void VerifyLicenseCommandLineArgs()
    {
        var args = Environment.GetCommandLineArgs();
        var hasAcceptLicense = false;
        foreach (var arg in args)
        {
            if (arg == "--accept-license")
            {
                hasAcceptLicense = true;
                break;
            }
        }

        if (string.Equals(Environment.GetEnvironmentVariable("DIGITALBRAIN_ACCEPT_LICENSE"), "true", StringComparison.OrdinalIgnoreCase))
        {
            hasAcceptLicense = true;
        }

        if (hasAcceptLicense)
        {
            Logger.LogInformation("LicenseNeuron: Terms and conditions accepted successfully.");
        }
        else
        {
            var termsSummary = """
================================================================================
                        DIGITALBRAIN DEVELOPER SUBSTRATE
                                TERMS OF SERVICE
================================================================================
By starting this software, you agree to the DigitalBrain Developer Substrate
License Agreement.

Key Terms:
1. Developer Use Only: This substrate is licensed solely for development and
   testing of AI-native applications.
2. No Modification of Kernel: The core InoLang interpreter and runtime are
   proprietary and must not be modified or decompiled.
3. Telemetry: Anonymous performance metrics and neuron execution counts are
   collected to improve the runtime.

To accept these terms and allow the system to boot, restart the application
with the '--accept-license' flag:
  dotnet run --project kernel/DigitalBrain.Boot -- --accept-license
  or
  dotnet-script digitalbrain.cs -- --accept-license
================================================================================
""";
            Console.Error.WriteLine(termsSummary);
            Console.WriteLine(termsSummary);
            throw new InvalidOperationException("Startup prevented: DigitalBrain Developer Substrate License terms must be accepted using '--accept-license'.");
        }
    }

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
        Logger.LogInformation("Generated new persistent ECDSA key pair for LicenseNeuron.");
    }

    public async Task<string> IssueLicenseAsync(string bundleId, string userId)
    {
        await EnsureKeysAsync();

        var payload = new
        {
            bundleId = bundleId,
            userId = userId,
            issuedAt = DateTimeOffset.UtcNow.ToString("O"),
            nonce = Guid.NewGuid().ToString()
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        var signature = BundleSignatureVerifier.SignData(payloadJson, _privateKey!);
        var signatureBase64 = Convert.ToBase64String(signature);

        var tokenData = new
        {
            payload = payloadJson,
            signature = signatureBase64,
            publicKey = Convert.ToBase64String(_publicKey!)
        };

        var tokenJson = JsonSerializer.Serialize(tokenData);
        var tokenBytes = Encoding.UTF8.GetBytes(tokenJson);
        var tokenBase64 = Convert.ToBase64String(tokenBytes);

        // Store active license record in the DB Neuron
        var db = Grains.GetGrain<IPostgresDbNeuron>("marketplace-db");
        await db.InsertLicenseAsync(new LicenseRow(tokenBase64, bundleId, userId, DateTimeOffset.UtcNow));

        Logger.LogInformation("Successfully issued license grant for user '{UserId}' on '{BundleId}'", userId, bundleId);
        return tokenBase64;
    }

    public async Task<bool> VerifyLicenseAsync(string licenseToken, string bundleId, string userId)
    {
        try
        {
            if (string.IsNullOrEmpty(licenseToken)) return false;

            var tokenBytes = Convert.FromBase64String(licenseToken);
            var tokenJson = Encoding.UTF8.GetString(tokenBytes);

            using var doc = JsonDocument.Parse(tokenJson);
            var root = doc.RootElement;
            var payloadJson = root.GetProperty("payload").GetString()!;
            var signatureBase64 = root.GetProperty("signature").GetString()!;
            var publicKeyBase64 = root.GetProperty("publicKey").GetString()!;

            var signature = Convert.FromBase64String(signatureBase64);
            var publicKey = Convert.FromBase64String(publicKeyBase64);

            // 1. Verify cryptographic integrity of token
            var isCryptographicallyValid = BundleSignatureVerifier.VerifyData(payloadJson, signature, publicKey);
            if (!isCryptographicallyValid)
            {
                Logger.LogWarning("License verification failed: cryptographic signature mismatch.");
                return false;
            }

            // 2. Verify payload details match
            using var payloadDoc = JsonDocument.Parse(payloadJson);
            var payloadRoot = payloadDoc.RootElement;
            var tokenBundleId = payloadRoot.GetProperty("bundleId").GetString();
            var tokenUserId = payloadRoot.GetProperty("userId").GetString();

            if (!string.Equals(tokenBundleId, bundleId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(tokenUserId, userId, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogWarning("License verification failed: payload mismatch. Expected {Bundle}/{User}, got {TokenBundle}/{TokenUser}",
                    bundleId, userId, tokenBundleId, tokenUserId);
                return false;
            }

            // 3. Confirm against DB
            var db = Grains.GetGrain<IPostgresDbNeuron>("marketplace-db");
            var matchingLicenses = await db.SelectLicensesAsync(userId, bundleId);
            if (!matchingLicenses.Any(l => l.LicenseToken == licenseToken))
            {
                Logger.LogWarning("License verification failed: token not found in PostgresDbNeuron registry.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error occurred during license token verification.");
            return false;
        }
    }

    // ==========================================
    // SYNAPSE STREAM SIGNAL HANDLERS
    // ==========================================

    protected override async Task HandleSynapseAsync(Synapse s)
    {
        switch (s)
        {
            case IssueLicenseCommand issue:
                var token = await IssueLicenseAsync(issue.BundleId, issue.UserId);
                await FireSynapseAsync(new IssueLicenseResponse(LicenseToken: token) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: issue.CorrelationId,
            causationId: issue.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(LicenseNeuron),
            receiverNeuronId: issue.CallerNeuronId,
            receiverNeuronType: issue.CallerNeuronType ?? string.Empty,
            timestamp: DateTimeOffset.UtcNow
        ) });
                break;

            case VerifyLicenseQuery verify:
                var isValid = await VerifyLicenseAsync(verify.LicenseToken, verify.BundleId, verify.UserId);
                await FireSynapseAsync(new VerifyLicenseResponse(IsValid: isValid,
        Reason: isValid ? "Verified successfully" : "Cryptographic verification or database entitlement check failed") { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: verify.CorrelationId,
            causationId: verify.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(LicenseNeuron),
            receiverNeuronId: verify.CallerNeuronId,
            receiverNeuronType: verify.CallerNeuronType ?? string.Empty,
            timestamp: DateTimeOffset.UtcNow
        ) });
                break;
        }
    }
}
