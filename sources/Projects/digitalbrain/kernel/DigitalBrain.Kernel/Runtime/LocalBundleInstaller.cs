using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Kernel.Runtime.Neurons;
using DigitalBrain.InoLang;
using DigitalBrain.InoLang.Linking;
using DigitalBrain.Runtime;

namespace DigitalBrain.Kernel.Runtime;

/// <summary>
/// Handles local installation of .bdom bundles by unpacking, compiling InoLang, and registering neurons dynamically.
/// </summary>
public sealed class LocalBundleInstaller : IBundleInstaller
{
    private readonly InterpretedNeuronRegistry _registry;
    private readonly IContractCatalog _contractCatalog;
    private readonly ILogger<LocalBundleInstaller> _logger;
    private readonly IGrainFactory? _grainFactory;

    public bool AllowUnsigned { get; set; }

    public LocalBundleInstaller(
        InterpretedNeuronRegistry registry,
        IContractCatalog contractCatalog,
        ILogger<LocalBundleInstaller> logger,
        IGrainFactory? grainFactory = null,
        IConfiguration? configuration = null)
    {
        _registry = registry;
        _contractCatalog = contractCatalog;
        _logger = logger;
        _grainFactory = grainFactory;
        AllowUnsigned = configuration?.GetValue<bool>("DigitalBrain:Marketplace:AllowUnsigned") ?? false;
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
        public string? LicenseToken { get; set; }
        public List<BundleManifestNeuron>? Neurons { get; set; }
    }

    public async Task<BundleInstallResult> InstallLocalAsync(string bundleFilePath, CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        _logger.LogInformation("Starting local install for bundle: {Path}", bundleFilePath);

        try
        {
            if (Directory.Exists(bundleFilePath))
            {
                // Support directory-based install for local development convenience
                return await InstallFromDirectoryAsync(bundleFilePath, diagnostics, cancellationToken);
            }

            if (!File.Exists(bundleFilePath))
            {
                diagnostics.Add($"Bundle file not found: {bundleFilePath}");
                return new BundleInstallResult(false, string.Empty, Array.Empty<string>(), [.. diagnostics]);
            }

            return await InstallFromZipAsync(bundleFilePath, diagnostics, cancellationToken);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Fatal installer error: {ex.Message}");
            _logger.LogError(ex, "Fatal error occurred during local bundle installation.");
            return new BundleInstallResult(false, string.Empty, Array.Empty<string>(), [.. diagnostics]);
        }
    }

    private async Task<BundleInstallResult> InstallFromZipAsync(
        string zipPath,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry is null)
        {
            diagnostics.Add("Missing 'manifest.json' in bundle archive.");
            return new BundleInstallResult(false, string.Empty, Array.Empty<string>(), [.. diagnostics]);
        }

        string manifestJson;
        using (var reader = new StreamReader(manifestEntry.Open()))
        {
            manifestJson = await reader.ReadToEndAsync(cancellationToken);
        }

        var manifest = JsonSerializer.Deserialize<BundleManifest>(
            manifestJson,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        if (manifest?.BundleId is null || manifest.Neurons is null)
        {
            diagnostics.Add("Malformed 'manifest.json' file.");
            return new BundleInstallResult(false, string.Empty, Array.Empty<string>(), [.. diagnostics]);
        }

        // The buyer's license rides in a separate license.dat entry so the
        // publisher-signed manifest.json stays byte-identical (signature verifies
        // over manifestJson, which never carries a per-buyer token). Fold it into
        // the manifest model for the entitlement check below.
        var licenseEntry = archive.GetEntry("license.dat");
        if (licenseEntry is not null)
        {
            using var licenseReader = new StreamReader(licenseEntry.Open());
            manifest.LicenseToken = (await licenseReader.ReadToEndAsync(cancellationToken)).Trim();
        }

        // --- PRE-SCAN AND SECURITY GATE ---
        var scannedPrice = "free";
        var scannedLicense = "source-included";
        var isPremium = false;
        var requiredDependencies = new List<string>();

        foreach (var neuron in manifest.Neurons)
        {
            if (string.IsNullOrWhiteSpace(neuron.Fqn) || string.IsNullOrWhiteSpace(neuron.SourcePath))
            {
                continue;
            }

            var cleanPath = neuron.SourcePath.Replace('\\', '/');
            var sourceEntry = archive.GetEntry(cleanPath);
            if (sourceEntry is null)
            {
                diagnostics.Add($"Neuron source path '{neuron.SourcePath}' not found in bundle archive.");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }

            using var sourceStream = sourceEntry.Open();
            using var reader = new StreamReader(sourceStream);
            var source = await reader.ReadToEndAsync(cancellationToken);

            var meta = InoMetadataScanner.Scan(source);
            if (!string.Equals(meta.Price, "free", StringComparison.OrdinalIgnoreCase))
            {
                isPremium = true;
                scannedPrice = meta.Price;
            }
            if (!string.Equals(meta.License, "source-included", StringComparison.OrdinalIgnoreCase))
            {
                scannedLicense = meta.License;
            }
            foreach (var req in meta.Requires)
            {
                if (!requiredDependencies.Contains(req))
                {
                    requiredDependencies.Add(req);
                }
            }
        }

        // Verify dependencies
        foreach (var req in requiredDependencies)
        {
            var resolved = _contractCatalog.Resolve(req);
            if (resolved is null)
            {
                diagnostics.Add($"Missing required dependency: '{req}' is not registered in the contract catalog.");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }
        }

        // Verify cryptographic signature if premium
        if (isPremium && !AllowUnsigned)
        {
            var signatureEntry = archive.GetEntry("signature.dat");
            if (signatureEntry is null)
            {
                diagnostics.Add("Premium bundle installation failed: signature.dat is required but missing.");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }

            string signatureBase64;
            using (var reader = new StreamReader(signatureEntry.Open()))
            {
                signatureBase64 = (await reader.ReadToEndAsync(cancellationToken)).Trim();
            }

            if (string.IsNullOrEmpty(signatureBase64))
            {
                diagnostics.Add("Premium bundle installation failed: signature.dat is empty.");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }

            if (_grainFactory is null)
            {
                diagnostics.Add("Premium bundle installation failed: Grain factory not available to fetch Marketplace public key.");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }

            var signatureBytes = Convert.FromBase64String(signatureBase64);
            var marketplaceNeuron = _grainFactory.GetGrain<IMarketplaceNeuron>(BrainScopeHelper.GetActiveScopedNeuronKey("marketplace-neuron"));
            var publicKey = await marketplaceNeuron.GetPublicKeyAsync();

            var signatureValid = BundleSignatureVerifier.VerifyData(manifestJson, signatureBytes, publicKey);
            if (!signatureValid)
            {
                diagnostics.Add("Premium bundle installation failed: cryptographic bundle signature verification failed.");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }

            // Verify License Entitlement via LicenseNeuron
            if (string.IsNullOrEmpty(manifest.LicenseToken))
            {
                diagnostics.Add("Premium bundle installation failed: licenseToken is missing from manifest.");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }

            // Extract userId from LicenseToken to perform license server verification
            string userId;
            try
            {
                var tokenBytes = Convert.FromBase64String(manifest.LicenseToken);
                var tokenJson = Encoding.UTF8.GetString(tokenBytes);
                using var tokenDoc = JsonDocument.Parse(tokenJson);
                var tokenRoot = tokenDoc.RootElement;
                var payloadJson = tokenRoot.GetProperty("payload").GetString()!;
                using var payloadDoc = JsonDocument.Parse(payloadJson);
                var payloadRoot = payloadDoc.RootElement;
                userId = payloadRoot.GetProperty("userId").GetString()!;
            }
            catch (Exception ex)
            {
                diagnostics.Add($"Premium bundle installation failed: licenseToken is malformed ({ex.Message}).");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }

            var licenseNeuron = _grainFactory.GetGrain<ILicenseNeuron>(BrainScopeHelper.GetActiveScopedNeuronKey("license-server"));
            var licenseValid = await licenseNeuron.VerifyLicenseAsync(manifest.LicenseToken, manifest.BundleId, userId);
            if (!licenseValid)
            {
                diagnostics.Add("Premium bundle installation failed: license verification failed or purchase entitlement not found.");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }
        }

        var registeredFqns = new List<string>();

        foreach (var neuron in manifest.Neurons)
        {
            if (string.IsNullOrWhiteSpace(neuron.Fqn) || string.IsNullOrWhiteSpace(neuron.SourcePath))
            {
                diagnostics.Add("Skipping invalid neuron declaration in manifest.");
                continue;
            }

            var cleanPath = neuron.SourcePath.Replace('\\', '/');
            var sourceEntry = archive.GetEntry(cleanPath);
            if (sourceEntry is null)
            {
                diagnostics.Add($"Neuron source path '{neuron.SourcePath}' not found in bundle archive.");
                continue;
            }

            using var sourceStream = sourceEntry.Open();
            using var reader = new StreamReader(sourceStream);
            var source = await reader.ReadToEndAsync(cancellationToken);

            var registration = CompileAndBuildRegistration(neuron.Fqn, source, diagnostics);
            if (registration is not null)
            {
                await _registry.RegisterDynamicAsync(registration);
                registeredFqns.Add(neuron.Fqn);
            }
        }

        bool success = registeredFqns.Count > 0;
        return new BundleInstallResult(success, manifest.BundleId, [.. registeredFqns], [.. diagnostics]);
    }

    private async Task<BundleInstallResult> InstallFromDirectoryAsync(
        string dirPath,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(dirPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            diagnostics.Add("Missing 'manifest.json' in target directory.");
            return new BundleInstallResult(false, string.Empty, Array.Empty<string>(), [.. diagnostics]);
        }

        var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var manifest = JsonSerializer.Deserialize<BundleManifest>(
            manifestJson,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        if (manifest?.BundleId is null || manifest.Neurons is null)
        {
            diagnostics.Add("Malformed 'manifest.json' file.");
            return new BundleInstallResult(false, string.Empty, Array.Empty<string>(), [.. diagnostics]);
        }

        // --- PRE-SCAN AND SECURITY GATE ---
        var scannedPrice = "free";
        var scannedLicense = "source-included";
        var isPremium = false;
        var requiredDependencies = new List<string>();

        foreach (var neuron in manifest.Neurons)
        {
            if (string.IsNullOrWhiteSpace(neuron.Fqn) || string.IsNullOrWhiteSpace(neuron.SourcePath))
            {
                continue;
            }

            var fullSourcePath = Path.Combine(dirPath, neuron.SourcePath);
            if (!File.Exists(fullSourcePath))
            {
                diagnostics.Add($"Neuron source file '{neuron.SourcePath}' not found on disk.");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }

            var source = await File.ReadAllTextAsync(fullSourcePath, cancellationToken);

            var meta = InoMetadataScanner.Scan(source);
            if (!string.Equals(meta.Price, "free", StringComparison.OrdinalIgnoreCase))
            {
                isPremium = true;
                scannedPrice = meta.Price;
            }
            if (!string.Equals(meta.License, "source-included", StringComparison.OrdinalIgnoreCase))
            {
                scannedLicense = meta.License;
            }
            foreach (var req in meta.Requires)
            {
                if (!requiredDependencies.Contains(req))
                {
                    requiredDependencies.Add(req);
                }
            }
        }

        // Verify dependencies
        foreach (var req in requiredDependencies)
        {
            var resolved = _contractCatalog.Resolve(req);
            if (resolved is null)
            {
                diagnostics.Add($"Missing required dependency: '{req}' is not registered in the contract catalog.");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }
        }

        // Verify cryptographic signature if premium
        if (isPremium && !AllowUnsigned)
        {
            var signaturePath = Path.Combine(dirPath, "signature.dat");
            if (!File.Exists(signaturePath))
            {
                diagnostics.Add("Premium bundle installation failed: signature.dat is required but missing.");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }

            var signatureBase64 = (await File.ReadAllTextAsync(signaturePath, cancellationToken)).Trim();
            if (string.IsNullOrEmpty(signatureBase64))
            {
                diagnostics.Add("Premium bundle installation failed: signature.dat is empty.");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }

            if (_grainFactory is null)
            {
                diagnostics.Add("Premium bundle installation failed: Grain factory not available to fetch Marketplace public key.");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }

            var signatureBytes = Convert.FromBase64String(signatureBase64);
            var marketplaceNeuron = _grainFactory.GetGrain<IMarketplaceNeuron>(BrainScopeHelper.GetActiveScopedNeuronKey("marketplace-neuron"));
            var publicKey = await marketplaceNeuron.GetPublicKeyAsync();

            var signatureValid = BundleSignatureVerifier.VerifyData(manifestJson, signatureBytes, publicKey);
            if (!signatureValid)
            {
                diagnostics.Add("Premium bundle installation failed: cryptographic bundle signature verification failed.");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }

            // Verify License Entitlement via LicenseNeuron
            if (string.IsNullOrEmpty(manifest.LicenseToken))
            {
                diagnostics.Add("Premium bundle installation failed: licenseToken is missing from manifest.");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }

            // Extract userId from LicenseToken to perform license server verification
            string userId;
            try
            {
                var tokenBytes = Convert.FromBase64String(manifest.LicenseToken);
                var tokenJson = Encoding.UTF8.GetString(tokenBytes);
                using var tokenDoc = JsonDocument.Parse(tokenJson);
                var tokenRoot = tokenDoc.RootElement;
                var payloadJson = tokenRoot.GetProperty("payload").GetString()!;
                using var payloadDoc = JsonDocument.Parse(payloadJson);
                var payloadRoot = payloadDoc.RootElement;
                userId = payloadRoot.GetProperty("userId").GetString()!;
            }
            catch (Exception ex)
            {
                diagnostics.Add($"Premium bundle installation failed: licenseToken is malformed ({ex.Message}).");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }

            var licenseNeuron = _grainFactory.GetGrain<ILicenseNeuron>(BrainScopeHelper.GetActiveScopedNeuronKey("license-server"));
            var licenseValid = await licenseNeuron.VerifyLicenseAsync(manifest.LicenseToken, manifest.BundleId, userId);
            if (!licenseValid)
            {
                diagnostics.Add("Premium bundle installation failed: license verification failed or purchase entitlement not found.");
                return new BundleInstallResult(false, manifest.BundleId, Array.Empty<string>(), [.. diagnostics]);
            }
        }

        var registeredFqns = new List<string>();

        foreach (var neuron in manifest.Neurons)
        {
            if (string.IsNullOrWhiteSpace(neuron.Fqn) || string.IsNullOrWhiteSpace(neuron.SourcePath))
            {
                diagnostics.Add("Skipping invalid neuron declaration in manifest.");
                continue;
            }

            var fullSourcePath = Path.Combine(dirPath, neuron.SourcePath);
            if (!File.Exists(fullSourcePath))
            {
                diagnostics.Add($"Neuron source file '{neuron.SourcePath}' not found on disk.");
                continue;
            }

            var source = await File.ReadAllTextAsync(fullSourcePath, cancellationToken);
            var registration = CompileAndBuildRegistration(neuron.Fqn, source, diagnostics);
            if (registration is not null)
            {
                await _registry.RegisterDynamicAsync(registration);
                registeredFqns.Add(neuron.Fqn);
            }
        }

        bool success = registeredFqns.Count > 0;
        return new BundleInstallResult(success, manifest.BundleId, [.. registeredFqns], [.. diagnostics]);
    }

    private InterpretedNeuronRegistration? CompileAndBuildRegistration(
        string fqn,
        string source,
        List<string> diagnostics)
    {
        try
        {
            var compiled = InoCompiler.Compile(source, _contractCatalog);
            if (!compiled.Success || compiled.Linked is null)
            {
                diagnostics.Add($"Compilation failed for '{fqn}': {string.Join(";", compiled.Diagnostics.Select(d => d.Message))}");
                return null;
            }

            return LinkedPortCatalogContributor.BuildRegistration(source, compiled.Linked);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Failed to process '{fqn}': {ex.Message}");
            return null;
        }
    }
}
