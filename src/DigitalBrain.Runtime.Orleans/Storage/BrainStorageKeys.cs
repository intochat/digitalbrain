using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>Holds the root that wraps each brain's journal key. A host may supply hardware custody (a TPM) instead of the default.</summary>
public interface IBrainKeyCustody
{
    /// <summary>Wraps <paramref name="secret"/> for <paramref name="accountId"/> under the authenticated <paramref name="context"/>.</summary>
    byte[] Wrap(string accountId, string context, byte[] secret);

    /// <summary>Unwraps what <see cref="Wrap"/> produced with the same account and context.</summary>
    byte[] Unwrap(string accountId, string context, byte[] wrapped);
}

/// <summary>Where the brain journals live: <c>&lt;Home&gt;/state</c>, set from <c>IntoChatOS:Home</c> by <c>AddBrainStorage</c>.</summary>
public sealed class BrainStoreOptions
{
    /// <summary>The brain store directory.</summary>
    public string Directory { get; set; } = StateHome.Default().State;
}

// Default custody: an HKDF-derived master per account from IntoChatOS:MasterKey (the os configured custody's derivation).
internal sealed class ConfiguredBrainKeyCustody(IConfiguration configuration) : IBrainKeyCustody
{
    public byte[] Wrap(string accountId, string context, byte[] secret) => SegmentSeal.Close(Master(accountId), context, secret);

    public byte[] Unwrap(string accountId, string context, byte[] wrapped) => SegmentSeal.Open(Master(accountId), context, wrapped);

    private byte[] Master(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        var root = configuration[IntoChatOSNames.MasterKeyKey];
        if (string.IsNullOrWhiteSpace(root)) { throw new InvalidOperationException($"{IntoChatOSNames.MasterKeyKey} is not set; the brain store cannot open a key."); }
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(root), 32, info: Encoding.UTF8.GetBytes($"master|{accountId}"));
    }
}

internal sealed class BrainStorageKeys(IBrainKeyCustody custody, IOptions<BrainStoreOptions> options)
{
    private readonly Lock gate = new();

    // The brain a grain key belongs to: its first '/'-segment, or the whole key.
    internal static string BrainOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var separator = key.IndexOf('/', StringComparison.Ordinal);
        return separator < 0 ? key : key[..separator];
    }

    internal string DirectoryOf(string brainId)
    {
        if (string.IsNullOrEmpty(brainId) || brainId.Length > 200 || brainId.Any(char.IsControl) || brainId.Contains('/', StringComparison.Ordinal))
        { throw new ArgumentException("Invalid brain scope.", nameof(brainId)); }
        return Path.Combine(Path.GetFullPath(options.Value.Directory), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(brainId))).ToLowerInvariant());
    }

    public Task<byte[]> GetOrCreate(string brainId, CancellationToken ct)
    {
        lock (gate)
        {
            ct.ThrowIfCancellationRequested();
            var directory = DirectoryOf(brainId);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "brain.key");
            var context = "brain-store-key|" + brainId;
            if (File.Exists(path)) { return Task.FromResult(custody.Unwrap(brainId, context, File.ReadAllBytes(path))); }
            if (Directory.EnumerateFiles(directory).Any(file => Path.GetExtension(file) is ".log" or ".snapshot"))
            { throw new CryptographicException("The brain key is missing; existing state must not be opened with a replacement key."); }
            var key = RandomNumberGenerator.GetBytes(32);
            var pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            BrainLog.WriteFlushed(pending, custody.Wrap(brainId, context, key));
            try { File.Move(pending, path); }
            catch (IOException) when (File.Exists(path)) { File.Delete(pending); return Task.FromResult(custody.Unwrap(brainId, context, File.ReadAllBytes(path))); }
            return Task.FromResult(key);
        }
    }
}
