using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DigitalBrain.Kernel.Hosting;

internal static class RuntimeStateKeyConfiguration
{
    private const string SectionPath = "DigitalBrain:Runtime:State";
    private static readonly byte[] DerivationSalt = Encoding.UTF8.GetBytes("digitalbrain-runtime-state-v1");

    public static RuntimeStateKeyRing Load(IConfiguration configuration, bool requireConfiguredKeys, bool production)
    {
        var section = configuration.GetSection(SectionPath);
        var activeText = section["ActiveKekVersion"];
        var signingText = section["SigningKey"];
        var kekEntries = section.GetSection("Keks").GetChildren().ToArray();
        var hasConfiguredMaterial = !string.IsNullOrWhiteSpace(activeText) || !string.IsNullOrWhiteSpace(signingText) || kekEntries.Length != 0;
        if (!hasConfiguredMaterial)
        {
            if (requireConfiguredKeys)
                throw new InvalidOperationException($"{SectionPath} key material is required for hosted or Production execution.");
            return CreateEphemeralLocalRing();
        }

        if (!int.TryParse(activeText, NumberStyles.None, CultureInfo.InvariantCulture, out var activeVersion) || activeVersion < 1)
            throw new InvalidOperationException($"{SectionPath}:ActiveKekVersion must be a positive integer.");
        if (string.IsNullOrWhiteSpace(signingText))
            throw new InvalidOperationException($"{SectionPath}:SigningKey is required when runtime-state keys are configured.");
        if (kekEntries.Length == 0)
            throw new InvalidOperationException($"{SectionPath}:Keks must contain at least one versioned key.");

        var keks = new Dictionary<int, byte[]>();
        byte[]? signingKey = null;
        try
        {
            foreach (var entry in kekEntries)
            {
                if (!int.TryParse(entry.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
                    throw new InvalidOperationException($"{SectionPath}:Keks contains a non-positive version.");
                if (!keks.TryAdd(version, DecodeKek(entry.Value, version, production)))
                    throw new InvalidOperationException($"{SectionPath}:Keks contains a duplicate version.");
            }
            if (!keks.ContainsKey(activeVersion))
                throw new InvalidOperationException($"{SectionPath}:Keks does not contain the active version.");
            signingKey = DecodeBase64(signingText, $"{SectionPath}:SigningKey");
            if (signingKey.Length < 32)
                throw new InvalidOperationException($"{SectionPath}:SigningKey must decode to at least 32 bytes.");
            return new RuntimeStateKeyRing(activeVersion, keks, signingKey);
        }
        finally
        {
            foreach (var key in keks.Values) CryptographicOperations.ZeroMemory(key);
            if (signingKey is not null) CryptographicOperations.ZeroMemory(signingKey);
        }
    }

    private static RuntimeStateKeyRing CreateEphemeralLocalRing()
    {
        var kek = RandomNumberGenerator.GetBytes(32);
        var signingKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            return new RuntimeStateKeyRing(1, new Dictionary<int, byte[]> { [1] = kek }, signingKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
            CryptographicOperations.ZeroMemory(signingKey);
        }
    }

    private static byte[] DecodeKek(string? encoded, int version, bool production)
    {
        var decoded = DecodeBase64(encoded, $"{SectionPath}:Keks:{version}");
        if (decoded.Length == 32) return decoded;
        try
        {
            if (decoded.Length < 32)
                throw new InvalidOperationException($"{SectionPath}:Keks:{version} must decode to at least 32 bytes.");
            if (production)
                throw new InvalidOperationException($"{SectionPath}:Keks:{version} must decode to exactly 32 bytes in Production.");
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, decoded, 32, DerivationSalt, Encoding.UTF8.GetBytes($"kek:{version}"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }

    private static byte[] DecodeBase64(string? encoded, string configurationPath)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            throw new InvalidOperationException($"{configurationPath} is required.");
        try
        {
            return Convert.FromBase64String(encoded);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"{configurationPath} must be valid Base64.", exception);
        }
    }
}

internal static class RuntimeStateNamespace
{
    public static string Resolve(IConfiguration configuration) =>
        RuntimeStateStorageNames.NormalizeNamespace(configuration["DigitalBrain:Runtime:StorageNamespace"]);

    public static string Container(string storageNamespace, string kind) =>
        RuntimeStateStorageNames.Container(storageNamespace, kind);
}

internal sealed record RuntimeStateHealthMetadata(string BackendKind, string StorageNamespace, int SchemaVersion, int ActiveKekVersion);

internal sealed class RuntimeStateHealthCheck : IHealthCheck
{
    private readonly RuntimeStateHealthMetadata _metadata;

    public RuntimeStateHealthCheck(RuntimeStateHealthMetadata metadata)
    {
        _metadata = metadata;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["backendKind"] = _metadata.BackendKind,
            ["namespace"] = _metadata.StorageNamespace,
            ["schemaVersion"] = _metadata.SchemaVersion,
            ["keyVersion"] = _metadata.ActiveKekVersion
        };
        return Task.FromResult(HealthCheckResult.Healthy("Encrypted runtime state is configured.", data));
    }
}
