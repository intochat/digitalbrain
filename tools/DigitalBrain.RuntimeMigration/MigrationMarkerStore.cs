using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.RuntimeMigration;

public sealed class MigrationMarkerKeyRing : IDisposable, IRuntimeStateKeyRing
{
    private const string SectionPath = "DigitalBrain:Runtime:State";
    private static readonly byte[] RuntimeDerivationSalt = Encoding.UTF8.GetBytes("digitalbrain-runtime-state-v1");
    private readonly Dictionary<int, byte[]> _keks;
    private readonly byte[] _signingKey;

    private MigrationMarkerKeyRing(int activeVersion, Dictionary<int, byte[]> keks, byte[] signingKey)
    {
        ActiveVersion = activeVersion;
        _keks = keks;
        _signingKey = signingKey;
    }

    public int ActiveVersion { get; }
    public int ActiveKekVersion => ActiveVersion;
    public ReadOnlyMemory<byte> SigningKey => _signingKey;

    public bool TryGetKek(int version, out ReadOnlyMemory<byte> key)
    {
        if (_keks.TryGetValue(version, out var value))
        {
            key = value;
            return true;
        }
        key = default;
        return false;
    }

    public static MigrationMarkerKeyRing Load(IConfiguration configuration)
    {
        var profile = configuration["DigitalBrain:Profile"] ?? "Development";
        var production = profile.Equals("Production", StringComparison.OrdinalIgnoreCase);
        var section = configuration.GetSection(SectionPath);
        if (!int.TryParse(section["ActiveKekVersion"], NumberStyles.None, CultureInfo.InvariantCulture,
                out var activeVersion) || activeVersion < 1)
            throw new MigrationGapException("marker-active-key-invalid");
        var signingKey = Decode(section["SigningKey"], "marker-signing-key-invalid");
        if (signingKey.Length < 32)
        {
            CryptographicOperations.ZeroMemory(signingKey);
            throw new MigrationGapException("marker-signing-key-invalid");
        }

        var keys = new Dictionary<int, byte[]>();
        try
        {
            foreach (var entry in section.GetSection("Keks").GetChildren())
            {
                if (!int.TryParse(entry.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
                    version < 1 || keys.ContainsKey(version))
                    throw new MigrationGapException("marker-kek-invalid");
                var decoded = Decode(entry.Value, "marker-kek-invalid");
                try
                {
                    if (decoded.Length < 32 || production && decoded.Length != 32)
                        throw new MigrationGapException("marker-kek-invalid");
                    if (decoded.Length == signingKey.Length &&
                        CryptographicOperations.FixedTimeEquals(decoded, signingKey))
                        throw new MigrationGapException("marker-keys-not-distinct");
                    keys[version] = decoded.Length == 32
                        ? decoded.ToArray()
                        : HKDF.DeriveKey(
                            HashAlgorithmName.SHA256,
                            decoded,
                            32,
                            RuntimeDerivationSalt,
                            Encoding.UTF8.GetBytes($"kek:{version}"));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(decoded);
                }
            }
            if (!keys.ContainsKey(activeVersion)) throw new MigrationGapException("marker-active-key-unavailable");
            foreach (var key in keys.Values)
            {
                if (key.Length == signingKey.Length && CryptographicOperations.FixedTimeEquals(key, signingKey))
                    throw new MigrationGapException("marker-keys-not-distinct");
            }
            return new MigrationMarkerKeyRing(activeVersion, keys, signingKey);
        }
        catch
        {
            foreach (var key in keys.Values) CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(signingKey);
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var key in _keks.Values) CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(_signingKey);
    }

    private static byte[] Decode(string? value, string code)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16_384) throw new MigrationGapException(code);
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { throw new MigrationGapException(code); }
    }
}

public static class MigrationMarkerCodec
{
    public static BinaryData Encrypt(MigrationMarker marker, string binding, MigrationMarkerKeyRing keys)
    {
        try
        {
            return BinaryData.FromBytes(RuntimeMigrationMarkerCodec.Protect(ToRuntime(marker), binding, keys));
        }
        catch (RuntimeStateIntegrityException)
        {
            throw new MigrationGapException("marker-payload-invalid");
        }
    }

    public static MigrationMarker Decrypt(BinaryData data, string binding, MigrationMarkerKeyRing keys)
    {
        try
        {
            var marker = RuntimeMigrationMarkerCodec.Unprotect(data.ToMemory().Span, binding, keys);
            return new(
                marker.SchemaVersion,
                marker.SourceDigest,
                marker.MigrationId,
                marker.ExpectedDigest,
                marker.ConversationCount,
                marker.TurnCount,
                marker.ActiveOperationCount,
                marker.TerminalOperationCount);
        }
        catch (RuntimeStateIntegrityException)
        {
            throw new MigrationGapException("marker-authentication-failed");
        }
    }

    private static RuntimeMigrationMarker ToRuntime(MigrationMarker marker) => new(
        marker.SchemaVersion,
        marker.SourceDigest,
        marker.MigrationId,
        marker.ExpectedDigest,
        marker.ConversationCount,
        marker.TurnCount,
        marker.ActiveOperationCount,
        marker.TerminalOperationCount);
}

public sealed class MigrationMarkerStore : IDisposable
{
    private readonly BlobContainerClient _container;
    private readonly MigrationMarkerKeyRing _keys;
    private readonly string _blobName;
    private readonly string _binding;

    private MigrationMarkerStore(
        BlobServiceClient blobs,
        MigrationMarkerKeyRing keys,
        string storageNamespace)
    {
        _container = blobs.GetBlobContainerClient(RuntimeStateStorageNames.Container(
            storageNamespace,
            RuntimeStateStorageNames.MigrationContainerKind));
        _keys = keys;
        _blobName = RuntimeStateStorageNames.MigrationMarkerBlob(storageNamespace);
        _binding = RuntimeStateStorageNames.MigrationMarkerBinding(storageNamespace);
    }

    public static MigrationMarkerStore Create(IConfiguration configuration)
    {
        var options = new BlobClientOptions();
        options.Diagnostics.IsDistributedTracingEnabled = false;
        BlobServiceClient blobs;
        var connection = configuration.GetConnectionString("conversationstate") ??
                         configuration.GetConnectionString("runtime-conversations");
        if (!string.IsNullOrWhiteSpace(connection))
        {
            blobs = new BlobServiceClient(connection, options);
        }
        else
        {
            var configuredUri = configuration["DigitalBrain:RuntimeMigration:BlobServiceUri"];
            var accountName = configuration["DigitalBrain:Storage:AccountName"];
            Uri serviceUri;
            if (!string.IsNullOrWhiteSpace(configuredUri) &&
                Uri.TryCreate(configuredUri, UriKind.Absolute, out var parsed))
                serviceUri = parsed;
            else if (!string.IsNullOrWhiteSpace(accountName) &&
                     accountName.All(static character => char.IsAsciiLetterOrDigit(character)))
                serviceUri = new Uri($"https://{accountName}.blob.core.windows.net");
            else
                throw new MigrationGapException("marker-storage-unavailable");
            var production = string.Equals(
                configuration["DigitalBrain:Profile"],
                "Production",
                StringComparison.OrdinalIgnoreCase);
            if (serviceUri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(serviceUri.Host) ||
                !string.IsNullOrEmpty(serviceUri.UserInfo) || !string.IsNullOrEmpty(serviceUri.Query) ||
                !string.IsNullOrEmpty(serviceUri.Fragment) || production && serviceUri.Scheme != "https")
                throw new MigrationGapException("marker-storage-unavailable");
            TokenCredential credential = new DefaultAzureCredential();
            blobs = new BlobServiceClient(serviceUri, credential, options);
        }
        string storageNamespace;
        try
        {
            storageNamespace = RuntimeStateStorageNames.NormalizeNamespace(
                configuration["DigitalBrain:Runtime:StorageNamespace"]);
        }
        catch (ArgumentException)
        {
            throw new MigrationGapException("marker-storage-namespace-invalid");
        }
        return new MigrationMarkerStore(blobs, MigrationMarkerKeyRing.Load(configuration), storageNamespace);
    }

    public async Task EnsureAsync(MigrationMarker marker, CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlobClient(_blobName);
        await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var encrypted = MigrationMarkerCodec.Encrypt(marker, _binding, _keys);
        try
        {
            await blob.UploadAsync(encrypted, overwrite: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
        }
        var existing = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
        var persisted = MigrationMarkerCodec.Decrypt(existing.Value.Content, _binding, _keys);
        if (persisted != marker) throw new MigrationGapException("marker-conflict");
    }

    public void Dispose() => _keys.Dispose();
}
