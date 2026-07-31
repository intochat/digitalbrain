using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Abstractions;
using Orleans.Journaling;

namespace DigitalBrain.Security;

internal sealed class DurableProtectedPayloadStore(
    IDurableValue<byte[]> state,
    Func<ValueTask> commit,
    IDurablePayloadProtector protector,
    OwnerId owner,
    TimeProvider time) : IProtectedPayloadStore
{
    private const string PurposePrefix = "DigitalBrain.Security.ProtectedPayloadStore/v1/";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string purpose = PurposePrefix + owner.Value;

    public async ValueTask<ProtectedPayloadReference> StoreAsync(
        OwnerId storeOwner,
        ReadOnlyMemory<byte> plaintext,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(time);
        RequireBoundOwner(storeOwner);

        if (plaintext.IsEmpty)
        {
            throw new ArgumentException("Plaintext payload cannot be empty.", nameof(plaintext));
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Lifetime must be positive.");
        }

        var now = time.GetUtcNow();
        DateTimeOffset expiresAt;
        try
        {
            expiresAt = now + lifetime;
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, exception.Message);
        }

        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Lifetime must produce a future expiry.");
        }

        var id = Guid.NewGuid();
        var protectedBytes = protector.Protect(purpose, plaintext.Span);
        var entries = ReadEntries();
        entries[id] = new StoredEntry(expiresAt, protectedBytes);

        var previous = state.Value;
        byte[]? serialized = null;
        try
        {
            serialized = JsonSerializer.SerializeToUtf8Bytes(entries, JsonOptions);
            state.Value = serialized;
            await commit().ConfigureAwait(false);
        }
        catch
        {
            state.Value = previous;
            throw;
        }

        return new ProtectedPayloadReference(id, expiresAt);
    }

    public ValueTask<ReadOnlyMemory<byte>> LoadAsync(
        OwnerId loadOwner,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(time);
        RequireBoundOwner(loadOwner);

        if (reference.Id == Guid.Empty)
        {
            throw new CryptographicException("The protected payload reference is invalid.");
        }

        var entries = ReadEntries();
        if (!entries.TryGetValue(reference.Id, out var entry))
        {
            throw new CryptographicException("The protected payload reference is invalid.");
        }

        var now = time.GetUtcNow();
        if (entry.ExpiresAt <= now)
        {
            throw new CryptographicException("The protected payload reference is invalid.");
        }

        if (reference.ExpiresAt is { } referenceExpiry
            && (referenceExpiry != entry.ExpiresAt || referenceExpiry <= now))
        {
            throw new CryptographicException("The protected payload reference is invalid.");
        }

        if (entry.ProtectedPayload is not { Length: > 0 } protectedPayload)
        {
            throw new CryptographicException("The protected payload reference is invalid.");
        }

        var plaintext = protector.Unprotect(purpose, protectedPayload);
        return ValueTask.FromResult<ReadOnlyMemory<byte>>(plaintext);
    }

    private void RequireBoundOwner(OwnerId requested)
    {
        if (requested != owner)
        {
            throw new CryptographicException("The protected payload reference is invalid.");
        }
    }

    private Dictionary<Guid, StoredEntry> ReadEntries()
    {
        if (state.Value is not { Length: > 0 } bytes)
        {
            return new Dictionary<Guid, StoredEntry>();
        }

        var entries = JsonSerializer.Deserialize<Dictionary<Guid, StoredEntry>>(bytes, JsonOptions);
        return entries ?? new Dictionary<Guid, StoredEntry>();
    }

    private sealed record StoredEntry(DateTimeOffset ExpiresAt, byte[] ProtectedPayload);
}
