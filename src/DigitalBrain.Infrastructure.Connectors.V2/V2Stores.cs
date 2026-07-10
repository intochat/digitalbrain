using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Infrastructure.Connectors.V2;

public sealed record EncryptedSecretBlob(
    string OwnerHash,
    string Purpose,
    int KeyVersion,
    byte[] Nonce,
    byte[] Ciphertext,
    byte[] Tag,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public interface IEncryptedSecretBlobRepository
{
    Task<bool> TryCreateAsync(SecretRef reference, EncryptedSecretBlob blob, CancellationToken cancellationToken);
    Task<EncryptedSecretBlob?> GetAsync(SecretRef reference, CancellationToken cancellationToken);
    Task DeleteAsync(SecretRef reference, CancellationToken cancellationToken);
}

public sealed class InMemoryEncryptedSecretBlobRepository : IEncryptedSecretBlobRepository
{
    private readonly ConcurrentDictionary<SecretRef, EncryptedSecretBlob> _items = [];

    public IReadOnlyCollection<EncryptedSecretBlob> Snapshot => _items.Values.ToArray();

    public Task<bool> TryCreateAsync(SecretRef reference, EncryptedSecretBlob blob, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_items.TryAdd(reference, blob));
    }

    public Task<EncryptedSecretBlob?> GetAsync(SecretRef reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _items.TryGetValue(reference, out var blob);
        return Task.FromResult(blob);
    }

    public Task DeleteAsync(SecretRef reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _items.TryRemove(reference, out _);
        return Task.CompletedTask;
    }
}

public sealed class EncryptedSecretVault : ISecretVault
{
    private const int TagSize = 16;
    private readonly int _currentKeyVersion;
    private readonly IReadOnlyDictionary<int, byte[]> _keys;
    private readonly IEncryptedSecretBlobRepository _repository;
    private readonly IClock _clock;

    public EncryptedSecretVault(int currentKeyVersion, IReadOnlyDictionary<int, byte[]> keys, IEncryptedSecretBlobRepository repository, IClock? clock = null)
    {
        if (!keys.TryGetValue(currentKeyVersion, out var key) || key.Length != 32 || keys.Any(pair => pair.Value.Length != 32))
        {
            throw new ArgumentException("V2 credential encryption keys must be 256-bit AES keys.", nameof(keys));
        }

        _currentKeyVersion = currentKeyVersion;
        _keys = keys.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        _repository = repository;
        _clock = clock ?? SystemClock.Instance;
    }

    public async Task<SecretRef> WriteAsync(CredentialOwner owner, string purpose, SecretPayload payload, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        var reference = new SecretRef(Base64Url.Encode(RandomNumberGenerator.GetBytes(24)), 1, purpose);
        var ownerHash = OwnerHash(owner);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload.Values);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        var aad = Encoding.UTF8.GetBytes($"{V2ConnectorSchema.StorageNamespace}|{reference}|{ownerHash}");
        try
        {
            using var aes = new AesGcm(_keys[_currentKeyVersion], TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
            var blob = new EncryptedSecretBlob(ownerHash, purpose, _currentKeyVersion, nonce, ciphertext, tag, _clock.UtcNow, expiresAt);
            if (!await _repository.TryCreateAsync(reference, blob, cancellationToken))
            {
                throw new InvalidOperationException("A V2 secret reference collision occurred.");
            }

            return reference;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task<SecretPayload?> ReadAsync(CredentialOwner owner, SecretRef reference, CancellationToken cancellationToken)
    {
        var blob = await _repository.GetAsync(reference, cancellationToken);
        if (blob is null || blob.ExpiresAt <= _clock.UtcNow)
        {
            return null;
        }

        var expectedOwner = Encoding.UTF8.GetBytes(OwnerHash(owner));
        var actualOwner = Encoding.UTF8.GetBytes(blob.OwnerHash);
        if (expectedOwner.Length != actualOwner.Length || !CryptographicOperations.FixedTimeEquals(expectedOwner, actualOwner))
        {
            return null;
        }

        if (!_keys.TryGetValue(blob.KeyVersion, out var key))
        {
            throw new InvalidOperationException("V2 credential encryption key version is unavailable.");
        }

        var plaintext = new byte[blob.Ciphertext.Length];
        var aad = Encoding.UTF8.GetBytes($"{V2ConnectorSchema.StorageNamespace}|{reference}|{blob.OwnerHash}");
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(blob.Nonce, blob.Ciphertext, blob.Tag, plaintext, aad);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext)
                         ?? throw new CryptographicException("Credential secret payload was invalid.");
            return new SecretPayload(values);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public Task RetireAsync(CredentialOwner owner, SecretRef reference, CancellationToken cancellationToken)
        => _repository.DeleteAsync(reference, cancellationToken);

    private static string OwnerHash(CredentialOwner owner)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{owner.TenantId.Value}\n{owner.WorkspaceId.Value}\n{owner.Principal.Kind}\n{owner.Principal.Value}")));
}

public sealed class InMemoryCredentialMetadataRepository : ICredentialMetadataRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<CredentialRef, CredentialRecord> _records = [];

    public Task<CredentialRecord?> GetAsync(CredentialRef reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _records.TryGetValue(reference, out var record);
            return Task.FromResult(record);
        }
    }

    public Task<bool> TryCreateAsync(CredentialRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_records.ContainsKey(record.Reference)) return Task.FromResult(false);
            _records.Add(record.Reference, record);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryReplaceAsync(CredentialRecord record, long expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_records.TryGetValue(record.Reference, out var current) || current.Revision != expectedRevision) return Task.FromResult(false);
            _records[record.Reference] = record;
            return Task.FromResult(true);
        }
    }
}

public sealed class V2CredentialStore : IV2CredentialStore
{
    private readonly ICredentialMetadataRepository _metadata;
    private readonly ISecretVault _vault;
    private readonly IClock _clock;

    public V2CredentialStore(ICredentialMetadataRepository metadata, ISecretVault vault, IClock? clock = null)
    {
        _metadata = metadata;
        _vault = vault;
        _clock = clock ?? SystemClock.Instance;
    }

    public async Task<CredentialRecord?> GetAsync(CredentialOwner owner, CredentialRef reference, CancellationToken cancellationToken)
    {
        var record = await _metadata.GetAsync(reference, cancellationToken);
        return record is not null && record.Owner == owner ? record : null;
    }

    public async Task<SecretPayload?> ReadSecretAsync(CredentialOwner owner, CredentialRef reference, CancellationToken cancellationToken)
    {
        var record = await GetAsync(owner, reference, cancellationToken);
        return record is null ? null : await _vault.ReadAsync(owner, record.ActiveSecret, cancellationToken);
    }

    public async Task<CredentialRecord> CreateFromExchangeAsync(CredentialRef reference, string provider, CredentialOwner owner, ProviderTokenSet tokenSet, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(owner, reference, cancellationToken);
        if (existing is not null) return existing;

        var secret = await _vault.WriteAsync(owner, "provider-token", tokenSet.Secret, null, cancellationToken);
        var now = _clock.UtcNow;
        var record = new CredentialRecord(reference, 1, provider, owner, CredentialStatus.Connected, secret,
            tokenSet.GrantedScopes.Order(StringComparer.Ordinal).ToArray(), AccountHash(provider, tokenSet.ProviderAccountId),
            tokenSet.ResourceBaseUri, now, now, tokenSet.AccessTokenExpiresAt, null, null, null);
        if (!await _metadata.TryCreateAsync(record, cancellationToken))
        {
            await _vault.RetireAsync(owner, secret, cancellationToken);
            existing = await GetAsync(owner, reference, cancellationToken);
            if (existing is not null) return existing;
            throw new InvalidOperationException("Credential reference is owned by another V2 scope.");
        }

        return record;
    }

    public async Task<CredentialRecord?> TryAcquireLeaseAsync(CredentialOwner owner, CredentialRef reference, string leaseOwner, TimeSpan duration, CredentialStatus operationStatus, CancellationToken cancellationToken)
    {
        var current = await GetAsync(owner, reference, cancellationToken);
        if (current is null || current.Status.IsRevokedOrUnavailable()) return null;
        if (current.LeaseExpiresAt > _clock.UtcNow) return null;
        var next = current with { Revision = current.Revision + 1, Status = operationStatus, LeaseOwner = leaseOwner, LeaseExpiresAt = _clock.UtcNow + duration, UpdatedAt = _clock.UtcNow };
        return await _metadata.TryReplaceAsync(next, current.Revision, cancellationToken) ? next : null;
    }

    public async Task<CredentialRecord> RotateAsync(CredentialOwner owner, CredentialRef reference, long expectedRevision, ProviderTokenSet tokenSet, bool retainMissingRefreshToken, CancellationToken cancellationToken)
    {
        var current = await GetAsync(owner, reference, cancellationToken) ?? throw new UnauthorizedAccessException("Credential is outside the V2 owner scope.");
        if (current.Revision != expectedRevision) throw new InvalidOperationException("Credential refresh lease was lost.");
        var payload = tokenSet.Secret;
        if (retainMissingRefreshToken)
        {
            var prior = await _vault.ReadAsync(owner, current.ActiveSecret, cancellationToken) ?? throw new InvalidOperationException("Current credential secret is unavailable.");
            payload = payload.WithMissingValuesFrom(prior, OAuthSecretNames.RefreshToken);
        }

        var replacement = await _vault.WriteAsync(owner, "provider-token", payload, null, cancellationToken);
        var next = current with
        {
            Revision = current.Revision + 1,
            Status = CredentialStatus.Connected,
            ActiveSecret = replacement,
            GrantedScopes = tokenSet.GrantedScopes.Order(StringComparer.Ordinal).ToArray(),
            ProviderAccountHash = AccountHash(current.Provider, tokenSet.ProviderAccountId),
            ResourceBaseUri = tokenSet.ResourceBaseUri ?? current.ResourceBaseUri,
            AccessTokenExpiresAt = tokenSet.AccessTokenExpiresAt,
            UpdatedAt = _clock.UtcNow,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            SafeFailure = null
        };
        if (!await _metadata.TryReplaceAsync(next, current.Revision, cancellationToken))
        {
            await _vault.RetireAsync(owner, replacement, cancellationToken);
            throw new InvalidOperationException("Credential changed concurrently; replacement was not activated.");
        }

        await _vault.RetireAsync(owner, current.ActiveSecret, cancellationToken);
        return next;
    }

    public async Task MarkStatusAsync(CredentialOwner owner, CredentialRef reference, long expectedRevision, CredentialStatus status, string? safeFailure, CancellationToken cancellationToken)
    {
        var current = await GetAsync(owner, reference, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (current.Revision != expectedRevision) throw new InvalidOperationException("Credential changed concurrently.");
        var next = current with { Revision = current.Revision + 1, Status = status, SafeFailure = safeFailure, UpdatedAt = _clock.UtcNow, LeaseOwner = null, LeaseExpiresAt = null };
        if (!await _metadata.TryReplaceAsync(next, expectedRevision, cancellationToken)) throw new InvalidOperationException("Credential changed concurrently.");
    }

    public async Task MarkRevokedAsync(CredentialOwner owner, CredentialRef reference, long expectedRevision, CancellationToken cancellationToken)
    {
        var current = await GetAsync(owner, reference, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (current.Revision != expectedRevision) throw new InvalidOperationException("Credential changed concurrently.");
        var next = current with { Revision = current.Revision + 1, Status = CredentialStatus.Revoked, UpdatedAt = _clock.UtcNow, LeaseOwner = null, LeaseExpiresAt = null };
        if (!await _metadata.TryReplaceAsync(next, expectedRevision, cancellationToken)) throw new InvalidOperationException("Credential changed concurrently.");
        await _vault.RetireAsync(owner, current.ActiveSecret, cancellationToken);
    }

    private static string AccountHash(string provider, string accountId)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{provider}\n{accountId}")));
}

file static class CredentialStatusPatterns
{
    public static bool IsRevokedOrUnavailable(this CredentialStatus status)
        => status is CredentialStatus.Revoked or CredentialStatus.ReauthorizationRequired or CredentialStatus.OutcomeUnknown or CredentialStatus.Unavailable;
}

public sealed class InMemoryOAuthFlowStore : IOAuthFlowStore
{
    private readonly object _gate = new();
    private readonly Dictionary<OAuthFlowKey, OAuthFlowRecord> _flows = [];

    public Task<bool> TryCreateAsync(OAuthFlowRecord flow, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_flows.ContainsKey(flow.Key)) return Task.FromResult(false);
            _flows.Add(flow.Key, flow);
            return Task.FromResult(true);
        }
    }

    public Task<OAuthFlowRecord?> GetAsync(OAuthFlowKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) { _flows.TryGetValue(key, out var flow); return Task.FromResult(flow); }
    }

    public Task<OAuthFlowRecord> ClaimAndEnqueueAsync(OAuthFlowKey key, long expectedRevision, SecretRef codeRef, OAuthEffectIntent effect, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var current = Require(key);
            if (current.Status != OAuthFlowStatus.Started || current.Revision != expectedRevision) return Task.FromResult(current);
            var transitions = current.Transitions.Concat([
                new OAuthTransition(current.Transitions.Count + 1L, OAuthFlowStatus.Claimed, now),
                new OAuthTransition(current.Transitions.Count + 2L, OAuthFlowStatus.ExchangeQueued, now)
            ]).ToArray();
            var next = current with { Revision = current.Revision + 1, Status = OAuthFlowStatus.ExchangeQueued, CodeRef = codeRef, EffectId = effect.EffectId, ResultCredentialRef = effect.CredentialRef, Transitions = transitions };
            _flows[key] = next;
            return Task.FromResult(next);
        }
    }

    public Task<OAuthFlowRecord?> TryAcquireExchangeLeaseAsync(OAuthFlowKey key, string effectId, string leaseOwner, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var current = Require(key);
            if (current.EffectId != effectId) return Task.FromResult<OAuthFlowRecord?>(null);
            if (current.Status == OAuthFlowStatus.Exchanging && current.LeaseExpiresAt <= now)
            {
                var unknown = AddTransition(current, OAuthFlowStatus.OutcomeUnknown, now, "exchange-lease-expired") with { LeaseOwner = null, LeaseExpiresAt = null };
                _flows[key] = unknown;
                return Task.FromResult<OAuthFlowRecord?>(null);
            }
            if (current.Status is not (OAuthFlowStatus.ExchangeQueued or OAuthFlowStatus.RetryScheduled) || current.NextAttemptAt > now) return Task.FromResult<OAuthFlowRecord?>(null);
            var next = AddTransition(current, OAuthFlowStatus.Exchanging, now, null) with { LeaseOwner = leaseOwner, LeaseExpiresAt = now + leaseDuration };
            _flows[key] = next;
            return Task.FromResult<OAuthFlowRecord?>(next);
        }
    }

    public Task<OAuthFlowRecord> TransitionAsync(OAuthFlowKey key, long expectedRevision, OAuthFlowStatus status, DateTimeOffset now, string? safeReason, CredentialRef? credentialRef, DateTimeOffset? nextAttemptAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var current = Require(key);
            if (current.Revision != expectedRevision) throw new InvalidOperationException("OAuth flow changed concurrently.");
            var next = AddTransition(current, status, now, safeReason) with { ResultCredentialRef = credentialRef ?? current.ResultCredentialRef, NextAttemptAt = nextAttemptAt, LeaseOwner = null, LeaseExpiresAt = null, SafeFailure = safeReason };
            _flows[key] = next;
            return Task.FromResult(next);
        }
    }

    private OAuthFlowRecord Require(OAuthFlowKey key) => _flows.TryGetValue(key, out var flow) ? flow : throw new KeyNotFoundException("OAuth flow was not found.");
    private static OAuthFlowRecord AddTransition(OAuthFlowRecord current, OAuthFlowStatus status, DateTimeOffset now, string? reason)
        => current with { Revision = current.Revision + 1, Status = status, Transitions = current.Transitions.Concat([new OAuthTransition(current.Transitions.Count + 1L, status, now, reason)]).ToArray() };
}

public static class OAuthSecretNames
{
    public const string AuthorizationCode = "authorization_code";
    public const string CodeVerifier = "code_verifier";
    public const string AccessToken = "access_token";
    public const string RefreshToken = "refresh_token";
    public const string ClientSecret = "client_secret";
}
