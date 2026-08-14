using System.Text;
using Brain.Product.Abstractions.Authority;

namespace DigitalBrain.ProductHost.Secrets;

public interface ISecretStore
{
    Task PutAsync(
        ConnectionReference connection,
        SecretMaterial material,
        CancellationToken cancellationToken);
}

// The read surface is intentionally internal to the ProductHost. Provider bridge adapters
// live at this boundary and receive plaintext only for the duration of an upstream call.
internal interface IProviderSecretReader
{
    Task<SecretMaterial> GetAsync(
        ConnectionReference connection,
        CancellationToken cancellationToken);
}

public sealed class SecretMaterial : IDisposable
{
    private byte[]? _bytes;

    private SecretMaterial(byte[] bytes)
    {
        _bytes = bytes;
    }

    public static SecretMaterial FromUtf8(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new SecretMaterial(Encoding.UTF8.GetBytes(value));
    }

    internal ReadOnlyMemory<byte> CopyForProviderBridge()
    {
        ObjectDisposedException.ThrowIf(_bytes is null, this);
        return _bytes.ToArray();
    }

    public override string ToString() => "SecretMaterial { Value = [REDACTED] }";

    public void Dispose()
    {
        var bytes = Interlocked.Exchange(ref _bytes, null);
        if (bytes is not null)
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
        }

        GC.SuppressFinalize(this);
    }
}

public sealed class EncryptedSecretPayload
{
    private readonly byte[] _authenticationTag;
    private readonly byte[] _ciphertext;
    private readonly byte[] _nonce;

    public EncryptedSecretPayload(
        string keyId,
        ReadOnlyMemory<byte> ciphertext,
        ReadOnlyMemory<byte> nonce = default,
        ReadOnlyMemory<byte> authenticationTag = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        if (ciphertext.IsEmpty)
        {
            throw new ArgumentException("Encrypted secret payload cannot be empty.", nameof(ciphertext));
        }

        KeyId = keyId;
        _ciphertext = ciphertext.ToArray();
        _nonce = nonce.ToArray();
        _authenticationTag = authenticationTag.ToArray();
    }

    public string KeyId { get; }

    public ReadOnlyMemory<byte> Ciphertext => _ciphertext.ToArray();

    public ReadOnlyMemory<byte> Nonce => _nonce.ToArray();

    public ReadOnlyMemory<byte> AuthenticationTag => _authenticationTag.ToArray();

    public override string ToString() => $"EncryptedSecretPayload {{ KeyId = {KeyId}, Ciphertext = [REDACTED] }}";
}

public interface IEncryptedSecretObjectStore
{
    ValueTask PutAsync(
        string bucket,
        string objectKey,
        EncryptedSecretPayload payload,
        CancellationToken cancellationToken);

    ValueTask<EncryptedSecretPayload> GetAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken);
}

public interface IKeyEncryptionProvider
{
    string KeyId { get; }

    ValueTask<EncryptedSecretPayload> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken);

    ValueTask<ReadOnlyMemory<byte>> DecryptAsync(
        EncryptedSecretPayload payload,
        CancellationToken cancellationToken);
}
