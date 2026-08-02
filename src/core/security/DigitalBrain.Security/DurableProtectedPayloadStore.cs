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
    private const string PurposePrefix = "DigitalBrain.Security.ProtectedPayloadStore/v2/";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async ValueTask<ProtectedPayloadReference> StoreAsync(
        OwnerId storeOwner,
        NeuronId task,
        Guid attempt,
        ReadOnlyMemory<byte> plaintext,
        TimeSpan lifetime,
        CancellationToken cancellationToken,
        Guid stableEntryId = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(time);
        RequireBoundOwner(storeOwner);
        RequireTask(task);
        RequireAttempt(attempt);

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

        cancellationToken.ThrowIfCancellationRequested();
        var entries = ReadEntries();
        if (stableEntryId != Guid.Empty
            && entries.TryGetValue(stableEntryId, out var existing))
        {
            // Authenticated binding/content check always precedes any expiry branch. Exact reissue
            // returns the original reference/expiry (live or expired; non-resurrecting). Divergent
            // plaintext permanently refuses — never overwrite a stable id with mismatched material.
            return RequireStableIdExactMatch(
                stableEntryId,
                existing,
                storeOwner,
                task,
                attempt,
                plaintext);
        }

        var id = stableEntryId != Guid.Empty ? stableEntryId : Guid.NewGuid();
        var purpose = PurposeFor(storeOwner, task, attempt);
        cancellationToken.ThrowIfCancellationRequested();
        var protectedBytes = protector.Protect(purpose, plaintext.Span);
        entries[id] = new StoredEntry(
            expiresAt,
            protectedBytes,
            task.Type,
            task.Owner.Value,
            task.Name,
            attempt);

        var previous = state.Value;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.Value = JsonSerializer.SerializeToUtf8Bytes(entries, JsonOptions);
            cancellationToken.ThrowIfCancellationRequested();
            await commit().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
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
        NeuronId task,
        Guid attempt,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(time);
        RequireBoundOwner(loadOwner);
        RequireTask(task);
        RequireAttempt(attempt);

        if (reference.Id == Guid.Empty)
        {
            throw new CryptographicException("The protected payload reference is invalid.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var entries = ReadEntries();
        if (!entries.TryGetValue(reference.Id, out var entry))
        {
            throw new CryptographicException("The protected payload reference is invalid.");
        }

        if (!string.Equals(entry.TaskType, task.Type, StringComparison.Ordinal)
            || !string.Equals(entry.TaskOwner, task.Owner.Value, StringComparison.Ordinal)
            || !string.Equals(entry.TaskName, task.Name, StringComparison.Ordinal)
            || entry.Attempt != attempt)
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

        cancellationToken.ThrowIfCancellationRequested();
        var purpose = PurposeFor(loadOwner, task, attempt);
        var plaintext = protector.Unprotect(purpose, protectedPayload);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ReadOnlyMemory<byte>>(plaintext);
    }

    private ProtectedPayloadReference RequireStableIdExactMatch(
        Guid stableEntryId,
        StoredEntry existing,
        OwnerId storeOwner,
        NeuronId task,
        Guid attempt,
        ReadOnlyMemory<byte> plaintext)
    {
        if (!string.Equals(existing.TaskType, task.Type, StringComparison.Ordinal)
            || !string.Equals(existing.TaskOwner, task.Owner.Value, StringComparison.Ordinal)
            || !string.Equals(existing.TaskName, task.Name, StringComparison.Ordinal)
            || existing.Attempt != attempt)
        {
            throw new InvalidOperationException(
                $"Protected payload entry '{stableEntryId:N}' is already bound to a different task/attempt.");
        }

        if (existing.ProtectedPayload is not { Length: > 0 } protectedPayload)
        {
            throw new InvalidOperationException(
                $"Protected payload entry '{stableEntryId:N}' cannot be reissued with divergent content.");
        }

        byte[] existingPlaintext;
        try
        {
            existingPlaintext = protector.Unprotect(PurposeFor(storeOwner, task, attempt), protectedPayload);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                $"Protected payload entry '{stableEntryId:N}' cannot be reissued with divergent content.",
                exception);
        }

        if (existingPlaintext.Length != plaintext.Length
            || !CryptographicOperations.FixedTimeEquals(existingPlaintext, plaintext.Span))
        {
            throw new InvalidOperationException(
                $"Protected payload entry '{stableEntryId:N}' cannot be reissued with divergent content.");
        }

        return new ProtectedPayloadReference(stableEntryId, existing.ExpiresAt);
    }

    private void RequireBoundOwner(OwnerId requested)
    {
        if (requested != owner)
        {
            throw new CryptographicException("The protected payload reference is invalid.");
        }
    }

    private static string PurposeFor(OwnerId boundOwner, NeuronId task, Guid attempt)
        => PurposePrefix
            + boundOwner.Value
            + "/"
            + task.Type
            + "/"
            + task.Owner.Value
            + "/"
            + task.Name
            + "/"
            + attempt.ToString("N");

    private static void RequireTask(NeuronId task)
    {
        if (task == default
            || string.IsNullOrWhiteSpace(task.Type)
            || string.IsNullOrWhiteSpace(task.Name)
            || task.Owner == default)
        {
            throw new ArgumentException("Task neuron id is required.", nameof(task));
        }
    }

    private static void RequireAttempt(Guid attempt)
    {
        if (attempt == Guid.Empty)
        {
            throw new ArgumentException("Attempt id is required.", nameof(attempt));
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

    private sealed record StoredEntry(
        DateTimeOffset ExpiresAt,
        byte[] ProtectedPayload,
        string TaskType,
        string TaskOwner,
        string TaskName,
        Guid Attempt);
}
