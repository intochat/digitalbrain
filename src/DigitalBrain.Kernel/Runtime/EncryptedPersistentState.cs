using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Kernel.Runtime;
using Orleans.Runtime;

namespace DigitalBrain.Kernel;

public sealed class RuntimeStateKeyRing : IRuntimeStateKeyRing
{
    private readonly IReadOnlyDictionary<int, byte[]> _keks;
    private readonly byte[] _signingKey;

    public RuntimeStateKeyRing(int activeKekVersion, IReadOnlyDictionary<int, byte[]> keks, byte[] signingKey)
    {
        if (activeKekVersion < 1 || !keks.TryGetValue(activeKekVersion, out var active) || active.Length != 32)
            throw new ArgumentException("The active runtime-state KEK must be a registered AES-256 key.", nameof(activeKekVersion));
        if (keks.Count == 0 || keks.Any(pair => pair.Key < 1 || pair.Value is not { Length: 32 }))
            throw new ArgumentException("Every runtime-state KEK must have a positive version and be 256 bits.", nameof(keks));
        if (signingKey is not { Length: >= 32 })
            throw new ArgumentException("The runtime-state signing key must be at least 256 bits and separate from the KEK ring.", nameof(signingKey));
        if (keks.Any(pair => CryptographicOperations.FixedTimeEquals(pair.Value, signingKey)))
            throw new ArgumentException("The runtime-state signing key must use material distinct from every KEK.", nameof(signingKey));
        ActiveKekVersion = activeKekVersion;
        _keks = keks.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToArray());
        _signingKey = signingKey.ToArray();
    }

    public int ActiveKekVersion { get; }
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
}

public sealed class EncryptedRuntimeStateProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int DataEncryptionKeySize = 32;
    private const int MaximumCiphertextBytes = 4 * 1024 * 1024;
    private readonly IRuntimeStateKeyRing _keys;
    private readonly JsonSerializerOptions _json;

    public EncryptedRuntimeStateProtector(IRuntimeStateKeyRing keys, JsonSerializerOptions? json = null)
    {
        _keys = keys;
        if (!_keys.TryGetKek(_keys.ActiveKekVersion, out var active) || active.Length != DataEncryptionKeySize ||
            _keys.SigningKey.Length < 32)
            throw new ArgumentException("Runtime-state key material is incomplete.", nameof(keys));
        if (CryptographicOperations.FixedTimeEquals(active.Span, _keys.SigningKey.Span))
            throw new ArgumentException("Runtime-state KEK and signing material must be distinct.", nameof(keys));
        _json = json is null ? CreateJson() : new JsonSerializerOptions(json);
        _json.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        _json.MaxDepth = Math.Min(_json.MaxDepth == 0 ? 64 : _json.MaxDepth, 64);
    }

    public EncryptedRuntimeStateEnvelope Protect<TState>(
        string scopeHash,
        string aggregateKind,
        int schemaVersion,
        long revision,
        TState value)
    {
        var dataKey = RandomNumberGenerator.GetBytes(DataEncryptionKeySize);
        try
        {
            return Seal(scopeHash, aggregateKind, schemaVersion, revision, value, dataKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    public TState Unprotect<TState>(
        string scopeHash,
        string aggregateKind,
        int schemaVersion,
        EncryptedRuntimeStateEnvelope envelope)
    {
        using var opened = Open<TState>(scopeHash, aggregateKind, schemaVersion, envelope);
        return opened.Value;
    }

    public bool RequiresRewrap(EncryptedRuntimeStateEnvelope envelope) =>
        envelope.KekVersion != _keys.ActiveKekVersion;

    internal OpenedRuntimeState<TState> Open<TState>(
        string scopeHash,
        string aggregateKind,
        int schemaVersion,
        EncryptedRuntimeStateEnvelope envelope)
    {
        RuntimeStateKeys.DemandScopeHash(scopeHash);
        DemandKind(aggregateKind);
        ValidateEnvelope(envelope, schemaVersion);
        if (!_keys.TryGetKek(envelope.KekVersion, out var kek))
            throw new RuntimeStateIntegrityException("unknown key version");
        var aad = BuildAad(scopeHash, aggregateKind, envelope.SchemaVersion, envelope.Revision);
        var expectedSignature = Sign(aad, envelope);
        if (envelope.Signature.Length != expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(envelope.Signature, expectedSignature))
            throw new RuntimeStateIntegrityException("invalid envelope signature");

        var dataKey = new byte[DataEncryptionKeySize];
        var plaintext = new byte[envelope.PayloadCiphertext.Length];
        try
        {
            using (var wrapper = new AesGcm(kek.Span, TagSize))
                wrapper.Decrypt(
                    envelope.WrappedDekNonce,
                    envelope.WrappedDekCiphertext,
                    envelope.WrappedDekTag,
                    dataKey,
                    AppendPurpose(aad, "dek"));
            using (var payload = new AesGcm(dataKey, TagSize))
                payload.Decrypt(
                    envelope.PayloadNonce,
                    envelope.PayloadCiphertext,
                    envelope.PayloadTag,
                    plaintext,
                    AppendPurpose(aad, "payload"));
            var value = JsonSerializer.Deserialize<TState>(plaintext, _json)
                        ?? throw new RuntimeStateIntegrityException("empty plaintext state");
            return new(value, dataKey, envelope.KekVersion != _keys.ActiveKekVersion);
        }
        catch (RuntimeStateIntegrityException)
        {
            CryptographicOperations.ZeroMemory(dataKey);
            throw;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or NotSupportedException)
        {
            CryptographicOperations.ZeroMemory(dataKey);
            throw new RuntimeStateIntegrityException("authentication or schema validation failed");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal EncryptedRuntimeStateEnvelope Seal<TState>(
        string scopeHash,
        string aggregateKind,
        int schemaVersion,
        long revision,
        TState value,
        ReadOnlySpan<byte> dataKey)
    {
        RuntimeStateKeys.DemandScopeHash(scopeHash);
        DemandKind(aggregateKind);
        if (schemaVersion < 1 || revision < 0 || dataKey.Length != DataEncryptionKeySize)
            throw new ArgumentException("Runtime-state schema, revision, or DEK is invalid.");
        if (!_keys.TryGetKek(_keys.ActiveKekVersion, out var kek))
            throw new RuntimeStateIntegrityException("active key unavailable");
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, _json);
        if (plaintext.Length > MaximumCiphertextBytes)
            throw new InvalidOperationException("Encrypted runtime state exceeds the persistence bound.");
        var aad = BuildAad(scopeHash, aggregateKind, schemaVersion, revision);
        try
        {
            var envelope = new EncryptedRuntimeStateEnvelope
            {
                EnvelopeVersion = RuntimeStateSchemas.Envelope,
                KekVersion = _keys.ActiveKekVersion,
                SchemaVersion = schemaVersion,
                Revision = revision,
                WrappedDekNonce = RandomNumberGenerator.GetBytes(NonceSize),
                WrappedDekCiphertext = new byte[DataEncryptionKeySize],
                WrappedDekTag = new byte[TagSize],
                PayloadNonce = RandomNumberGenerator.GetBytes(NonceSize),
                PayloadCiphertext = new byte[plaintext.Length],
                PayloadTag = new byte[TagSize]
            };
            using (var wrapper = new AesGcm(kek.Span, TagSize))
                wrapper.Encrypt(
                    envelope.WrappedDekNonce,
                    dataKey,
                    envelope.WrappedDekCiphertext,
                    envelope.WrappedDekTag,
                    AppendPurpose(aad, "dek"));
            using (var payload = new AesGcm(dataKey, TagSize))
                payload.Encrypt(
                    envelope.PayloadNonce,
                    plaintext,
                    envelope.PayloadCiphertext,
                    envelope.PayloadTag,
                    AppendPurpose(aad, "payload"));
            envelope.Signature = Sign(aad, envelope);
            return envelope;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private byte[] Sign(ReadOnlySpan<byte> aad, EncryptedRuntimeStateEnvelope envelope)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        Write(writer, aad);
        writer.Write(envelope.EnvelopeVersion);
        writer.Write(envelope.KekVersion);
        writer.Write(envelope.SchemaVersion);
        writer.Write(envelope.Revision);
        Write(writer, envelope.WrappedDekNonce);
        Write(writer, envelope.WrappedDekCiphertext);
        Write(writer, envelope.WrappedDekTag);
        Write(writer, envelope.PayloadNonce);
        Write(writer, envelope.PayloadCiphertext);
        Write(writer, envelope.PayloadTag);
        writer.Flush();
        return HMACSHA256.HashData(_keys.SigningKey.Span, stream.ToArray());
    }

    private static byte[] BuildAad(string scopeHash, string aggregateKind, int schemaVersion, long revision)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        Write(writer, Encoding.UTF8.GetBytes("digitalbrain-runtime-state-aad-v1"));
        Write(writer, Encoding.ASCII.GetBytes(scopeHash));
        Write(writer, Encoding.UTF8.GetBytes(aggregateKind));
        writer.Write(schemaVersion);
        writer.Write(revision);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] AppendPurpose(ReadOnlySpan<byte> aad, string purpose)
    {
        var purposeBytes = Encoding.ASCII.GetBytes(purpose);
        var result = new byte[aad.Length + sizeof(int) + purposeBytes.Length];
        aad.CopyTo(result);
        BitConverter.TryWriteBytes(result.AsSpan(aad.Length, sizeof(int)), purposeBytes.Length);
        purposeBytes.CopyTo(result, aad.Length + sizeof(int));
        return result;
    }

    private static void ValidateEnvelope(EncryptedRuntimeStateEnvelope envelope, int schemaVersion)
    {
        if (envelope.EnvelopeVersion != RuntimeStateSchemas.Envelope || envelope.SchemaVersion != schemaVersion ||
            envelope.KekVersion < 1 || envelope.Revision < 0 ||
            envelope.WrappedDekNonce is not { Length: NonceSize } ||
            envelope.WrappedDekCiphertext is not { Length: DataEncryptionKeySize } ||
            envelope.WrappedDekTag is not { Length: TagSize } ||
            envelope.PayloadNonce is not { Length: NonceSize } ||
            envelope.PayloadTag is not { Length: TagSize } ||
            envelope.Signature is not { Length: 32 } ||
            envelope.PayloadCiphertext is null || envelope.PayloadCiphertext.Length > MaximumCiphertextBytes)
            throw new RuntimeStateIntegrityException("invalid envelope metadata");
    }

    private static void DemandKind(string aggregateKind)
    {
        if (aggregateKind is not (RuntimeStateKinds.Conversation or RuntimeStateKinds.ConversationArchive or
            RuntimeStateKinds.SurfaceFeed or RuntimeStateKinds.Session or RuntimeStateKinds.SynapseJournal or
            RuntimeStateKinds.InoEffectPlan))
            throw new ArgumentException("Unsupported encrypted runtime-state kind.", nameof(aggregateKind));
    }

    private static void Write(BinaryWriter writer, ReadOnlySpan<byte> bytes)
    {
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static JsonSerializerOptions CreateJson() => new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 64,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}

internal sealed class OpenedRuntimeState<TState>(TState value, byte[] dataKey, bool requiresRewrap) : IDisposable
{
    public TState Value { get; } = value;
    public byte[] DataKey { get; } = dataKey;
    public bool RequiresRewrap { get; } = requiresRewrap;
    public void Dispose() => CryptographicOperations.ZeroMemory(DataKey);
}

public sealed class EncryptedPersistentState<TState>
{
    private readonly IPersistentState<EncryptedRuntimeStateEnvelope> _persistentState;
    private readonly EncryptedRuntimeStateProtector _protector;
    private readonly string _scopeHash;
    private readonly string _aggregateKind;
    private readonly int _schemaVersion;
    private readonly Func<TState> _empty;
    private readonly Func<TState, long> _revision;
    private readonly Action<TState> _validate;
    private readonly Func<TState, TState, CancellationToken, Task>? _prepareCommit;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _poisoned;

    public EncryptedPersistentState(
        IPersistentState<EncryptedRuntimeStateEnvelope> persistentState,
        EncryptedRuntimeStateProtector protector,
        string scopeHash,
        string aggregateKind,
        int schemaVersion,
        Func<TState> empty,
        Func<TState, long> revision,
        Action<TState> validate,
        Func<TState, TState, CancellationToken, Task>? prepareCommit = null)
    {
        RuntimeStateKeys.DemandScopeHash(scopeHash);
        _persistentState = persistentState;
        _protector = protector;
        _scopeHash = scopeHash;
        _aggregateKind = aggregateKind;
        _schemaVersion = schemaVersion;
        _empty = empty;
        _revision = revision;
        _validate = validate;
        _prepareCommit = prepareCommit;
    }

    public async Task<TState> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemandUsable();
            using var opened = await OpenAsync(cancellationToken).ConfigureAwait(false);
            return opened.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<TState> UpdateAsync(
        long expectedRevision,
        Func<TState, TState> transition,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(expectedRevision, current =>
        {
            var next = transition(current);
            return (next, next);
        }, cancellationToken);

    public async Task<TResult> UpdateAsync<TResult>(
        long expectedRevision,
        Func<TState, (TState State, TResult Result)> transition,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemandUsable();
            using var opened = await OpenAsync(cancellationToken).ConfigureAwait(false);
            var currentRevision = _revision(opened.Value);
            if (currentRevision != expectedRevision)
                throw new RuntimeStateConflictException(expectedRevision, currentRevision);
            var (next, result) = transition(opened.Value);
            _validate(next);
            var nextRevision = _revision(next);
            if (nextRevision == currentRevision) return result;
            if (nextRevision != checked(currentRevision + 1))
                throw new InvalidOperationException("Runtime-state transitions must advance exactly one revision.");
            if (_prepareCommit is not null)
                await _prepareCommit(opened.Value, next, cancellationToken).ConfigureAwait(false);
            var dataKey = opened.DataKey;
            var generatedDataKey = dataKey.Length == 0;
            if (generatedDataKey) dataKey = RandomNumberGenerator.GetBytes(32);
            EncryptedRuntimeStateEnvelope envelope;
            try
            {
                envelope = _protector.Seal(
                    _scopeHash,
                    _aggregateKind,
                    _schemaVersion,
                    nextRevision,
                    next,
                    dataKey);
            }
            finally
            {
                if (generatedDataKey) CryptographicOperations.ZeroMemory(dataKey);
            }
            await WriteWithRollbackAsync(envelope).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<OpenedRuntimeState<TState>> OpenAsync(CancellationToken cancellationToken)
    {
        DemandUsable();
        cancellationToken.ThrowIfCancellationRequested();
        var envelope = _persistentState.State;
        if (!_persistentState.RecordExists)
        {
            if (envelope.EnvelopeVersion != 0)
                throw new RuntimeStateIntegrityException("uncommitted envelope present");
            var empty = _empty();
            _validate(empty);
            return new(empty, [], false);
        }
        if (envelope.EnvelopeVersion == 0)
            throw new RuntimeStateIntegrityException("missing persisted envelope");
        var opened = _protector.Open<TState>(_scopeHash, _aggregateKind, _schemaVersion, envelope);
        try
        {
            _validate(opened.Value);
            if (envelope.Revision != _revision(opened.Value))
                throw new RuntimeStateIntegrityException("envelope and domain revisions differ");
            if (opened.RequiresRewrap)
            {
                var rewrapped = _protector.Seal(
                    _scopeHash,
                    _aggregateKind,
                    _schemaVersion,
                    envelope.Revision,
                    opened.Value,
                    opened.DataKey);
                await WriteWithRollbackAsync(rewrapped).ConfigureAwait(false);
            }
            return opened;
        }
        catch
        {
            opened.Dispose();
            throw;
        }
    }

    private async Task WriteWithRollbackAsync(EncryptedRuntimeStateEnvelope next)
    {
        try
        {
            await PersistedStateReconciliation.WriteWithRollbackAsync(_persistentState, next, SameEnvelope)
                .ConfigureAwait(false);
        }
        catch (PersistedStateWriteOutcomeUnknownException)
        {
            _poisoned = true;
            throw;
        }
    }

    private void DemandUsable()
    {
        if (_poisoned)
            throw new RuntimeStateIntegrityException("storage write outcome for this activation is unknown");
    }

    private static bool SameEnvelope(
        EncryptedRuntimeStateEnvelope first,
        EncryptedRuntimeStateEnvelope second) =>
        first.EnvelopeVersion == second.EnvelopeVersion && first.KekVersion == second.KekVersion &&
        first.SchemaVersion == second.SchemaVersion && first.Revision == second.Revision &&
        first.WrappedDekNonce.AsSpan().SequenceEqual(second.WrappedDekNonce) &&
        first.WrappedDekCiphertext.AsSpan().SequenceEqual(second.WrappedDekCiphertext) &&
        first.WrappedDekTag.AsSpan().SequenceEqual(second.WrappedDekTag) &&
        first.PayloadNonce.AsSpan().SequenceEqual(second.PayloadNonce) &&
        first.PayloadCiphertext.AsSpan().SequenceEqual(second.PayloadCiphertext) &&
        first.PayloadTag.AsSpan().SequenceEqual(second.PayloadTag) &&
        first.Signature.AsSpan().SequenceEqual(second.Signature);
}
