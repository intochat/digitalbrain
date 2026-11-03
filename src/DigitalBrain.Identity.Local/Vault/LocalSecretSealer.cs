using DigitalBrain.Capabilities.Connections.Sealing;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Identity.Local.Custody;

namespace DigitalBrain.Identity.Local.Vault;

/// <summary>Seals and opens connection and brain-scoped secrets using <see cref="IMasterKeyCustody"/>.</summary>
public sealed class LocalSecretSealer(IMasterKeyCustody custody) : ISecretSealer, IBrainSecretSealer
{
    private const string BrainSecretContext = "brain-secret";

    private readonly IMasterKeyCustody _custody = custody ?? throw new ArgumentNullException(nameof(custody));
    private readonly MasterKeyWrapper _keys = new(custody ?? throw new ArgumentNullException(nameof(custody)));

    /// <inheritdoc />
    public SealedSecret Seal(string name, string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        var key = SecretCipher.NewKey();
        return new SealedSecret
        {
            KeyId = _keys.Wrap(key),
            Cipher = SecretCipher.Seal(key, plaintext),
        };
    }

    /// <inheritdoc />
    public string Open(SealedSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return SecretCipher.Open(_keys.Unwrap(secret.KeyId), secret.Cipher);
    }

    /// <inheritdoc />
    public Task<SealedSecret> Seal(string brainId, string name, string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brainId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        var dataKey = SecretCipher.NewKey();
        var wrappedKey = _custody.Wrap(brainId, BrainSecretContext, dataKey);
        return Task.FromResult(new SealedSecret
        {
            KeyId = BrainSeal.KeyPrefix + Convert.ToBase64String(wrappedKey),
            Cipher = SecretCipher.Seal(dataKey, plaintext),
        });
    }

    /// <inheritdoc />
    public Task<string> Open(string brainId, SealedSecret secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brainId);
        ArgumentNullException.ThrowIfNull(secret);
        if (!BrainSeal.IsBrainSealed(secret))
        {
            throw new ArgumentException("A brain-sealed secret is required.", nameof(secret));
        }

        var wrappedKey = Convert.FromBase64String(secret.KeyId[BrainSeal.KeyPrefix.Length..]);
        var dataKey = _custody.Unwrap(brainId, BrainSecretContext, wrappedKey);
        return Task.FromResult(SecretCipher.Open(dataKey, secret.Cipher));
    }
}
