using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Brain.Product.Abstractions.Authority;
using DigitalBrain.ProductHost.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalBrain.ProductHost.Secrets;

public sealed class EncryptedObjectSecretStore : ISecretStore, IProviderSecretReader
{
    private readonly IEncryptedSecretObjectStore _objects;
    private readonly IKeyEncryptionProvider _encryption;
    private readonly string _bucket;
    private readonly ILogger<EncryptedObjectSecretStore> _logger;

    public EncryptedObjectSecretStore(
        IEncryptedSecretObjectStore objects,
        IKeyEncryptionProvider encryption,
        IOptions<ProductStoreOptions> options,
        ILogger<EncryptedObjectSecretStore> logger)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bucket = options.Value.ObjectStoreBucket
            ?? throw new OptionsValidationException(
                nameof(ProductStoreOptions),
                typeof(ProductStoreOptions),
                ["An object-store bucket is required."]);
        if (!string.Equals(
            options.Value.ObjectStoreEncryptionKeyId,
            encryption.KeyId,
            StringComparison.Ordinal))
        {
            throw new OptionsValidationException(
                nameof(ProductStoreOptions),
                typeof(ProductStoreOptions),
                ["The configured object-store encryption key does not match the key-encryption provider."]);
        }
    }

    public async Task PutAsync(
        ConnectionReference connection,
        SecretMaterial material,
        CancellationToken cancellationToken)
    {
        if (connection.IsEmpty)
        {
            throw new ArgumentException("A connection reference is required.", nameof(connection));
        }

        ArgumentNullException.ThrowIfNull(material);
        cancellationToken.ThrowIfCancellationRequested();
        var objectKey = ObjectKey(connection);
        var plaintext = material.CopyForProviderBridge().ToArray();
        try
        {
            var encrypted = await _encryption.EncryptAsync(plaintext, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(encrypted.KeyId, _encryption.KeyId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The key-encryption provider returned an unexpected key id.");
            }

            await _objects.PutAsync(_bucket, objectKey, encrypted, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("Stored encrypted provider secret object {SecretObjectKey}.", objectKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    async Task<SecretMaterial> IProviderSecretReader.GetAsync(
        ConnectionReference connection,
        CancellationToken cancellationToken)
    {
        if (connection.IsEmpty)
        {
            throw new ArgumentException("A connection reference is required.", nameof(connection));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var encrypted = await _objects.GetAsync(
            _bucket,
            ObjectKey(connection),
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(encrypted.KeyId, _encryption.KeyId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The stored secret uses an unexpected encryption key.");
        }

        var plaintext = (await _encryption.DecryptAsync(encrypted, cancellationToken)
            .ConfigureAwait(false)).ToArray();
        try
        {
            return SecretMaterial.FromUtf8(Encoding.UTF8.GetString(plaintext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static string ObjectKey(ConnectionReference connection)
        => $"connections/{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connection.Value))).ToLowerInvariant()}";
}

internal sealed class DevelopmentSecretObjectStore : IEncryptedSecretObjectStore
{
    private readonly ConcurrentDictionary<(string Bucket, string Key), EncryptedSecretPayload> _objects = [];

    public ValueTask PutAsync(
        string bucket,
        string objectKey,
        EncryptedSecretPayload payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _objects[(bucket, objectKey)] = payload;
        return ValueTask.CompletedTask;
    }

    public ValueTask<EncryptedSecretPayload> GetAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_objects.TryGetValue((bucket, objectKey), out var payload)
            ? payload
            : throw new KeyNotFoundException("The encrypted provider secret was not found."));
    }
}

internal sealed class DevelopmentKeyEncryptionProvider : IKeyEncryptionProvider
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    internal DevelopmentKeyEncryptionProvider(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        KeyId = keyId;
        _key = SHA256.HashData(Encoding.UTF8.GetBytes($"digitalbrain-development-only/{keyId}"));
    }

    public string KeyId { get; }

    public ValueTask<EncryptedSecretPayload> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext.Span, ciphertext, tag);
        return ValueTask.FromResult(new EncryptedSecretPayload(KeyId, ciphertext, nonce, tag));
    }

    public ValueTask<ReadOnlyMemory<byte>> DecryptAsync(
        EncryptedSecretPayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(payload.KeyId, KeyId, StringComparison.Ordinal)
            || payload.Nonce.Length != NonceSize
            || payload.AuthenticationTag.Length != TagSize)
        {
            throw new CryptographicException("The encrypted secret envelope is invalid.");
        }

        var plaintext = new byte[payload.Ciphertext.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            payload.Nonce.Span,
            payload.Ciphertext.Span,
            payload.AuthenticationTag.Span,
            plaintext);
        return ValueTask.FromResult<ReadOnlyMemory<byte>>(plaintext);
    }
}
