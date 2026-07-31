using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Abstractions;
using Orleans.Journaling;

namespace DigitalBrain.Security;

internal sealed class DurableProtectedTriggerStore(
    IDurableValue<byte[]> state,
    Func<ValueTask> commit,
    IDurablePayloadProtector protector,
    OwnerId owner,
    TimeProvider time) : IProtectedTriggerStore
{
    private const string PurposePrefix = "DigitalBrain.Security.ProtectedTriggerStore/v1/";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async ValueTask<ProtectedPayloadReference> StoreAsync(
        OwnerId storeOwner,
        NeuronId task,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        string caseId,
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
        RequireTask(task);
        RequireActivation(behavior, revision, caseId);

        if (plaintext.IsEmpty)
        {
            throw new ArgumentException("Plaintext trigger cannot be empty.", nameof(plaintext));
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
        var purpose = PurposeFor(storeOwner, task, behavior, revision, caseId);
        var protectedBytes = protector.Protect(purpose, plaintext.Span);
        var entries = ReadEntries();
        entries[id] = new StoredEntry(
            expiresAt,
            protectedBytes,
            task.Type,
            task.Owner.Value,
            task.Name,
            behavior.Value,
            revision.Value,
            caseId);

        var previous = state.Value;
        try
        {
            state.Value = JsonSerializer.SerializeToUtf8Bytes(entries, JsonOptions);
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
        NeuronId task,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        string caseId,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(time);
        RequireBoundOwner(loadOwner);
        RequireTask(task);
        RequireActivation(behavior, revision, caseId);

        if (reference.Id == Guid.Empty)
        {
            throw new CryptographicException("The protected trigger reference is invalid.");
        }

        var entries = ReadEntries();
        if (!entries.TryGetValue(reference.Id, out var entry))
        {
            throw new CryptographicException("The protected trigger reference is invalid.");
        }

        if (!string.Equals(entry.TaskType, task.Type, StringComparison.Ordinal)
            || !string.Equals(entry.TaskOwner, task.Owner.Value, StringComparison.Ordinal)
            || !string.Equals(entry.TaskName, task.Name, StringComparison.Ordinal)
            || !string.Equals(entry.Behavior, behavior.Value, StringComparison.Ordinal)
            || !string.Equals(entry.Revision, revision.Value, StringComparison.Ordinal)
            || !string.Equals(entry.CaseId, caseId, StringComparison.Ordinal))
        {
            throw new CryptographicException("The protected trigger reference is invalid.");
        }

        var now = time.GetUtcNow();
        if (entry.ExpiresAt <= now)
        {
            throw new CryptographicException("The protected trigger reference is invalid.");
        }

        if (reference.ExpiresAt is { } referenceExpiry
            && (referenceExpiry != entry.ExpiresAt || referenceExpiry <= now))
        {
            throw new CryptographicException("The protected trigger reference is invalid.");
        }

        if (entry.ProtectedPayload is not { Length: > 0 } protectedPayload)
        {
            throw new CryptographicException("The protected trigger reference is invalid.");
        }

        var purpose = PurposeFor(loadOwner, task, behavior, revision, caseId);
        var plaintext = protector.Unprotect(purpose, protectedPayload);
        return ValueTask.FromResult<ReadOnlyMemory<byte>>(plaintext);
    }

    internal static string PurposeFor(
        OwnerId boundOwner,
        NeuronId task,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        string caseId)
        => PurposePrefix
            + boundOwner.Value
            + "/"
            + task.Type
            + "/"
            + task.Owner.Value
            + "/"
            + task.Name
            + "/"
            + behavior.Value
            + "/"
            + revision.Value
            + "/"
            + caseId;

    private void RequireBoundOwner(OwnerId requested)
    {
        if (requested != owner)
        {
            throw new CryptographicException("The protected trigger reference is invalid.");
        }
    }

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

    private static void RequireActivation(BehaviorId behavior, BehaviorRevisionId revision, string caseId)
    {
        if (string.IsNullOrWhiteSpace(behavior.Value))
        {
            throw new ArgumentException("Behavior id is required.", nameof(behavior));
        }

        if (string.IsNullOrWhiteSpace(revision.Value))
        {
            throw new ArgumentException("Revision is required.", nameof(revision));
        }

        revision.EnsureValid();

        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new ArgumentException("Case id is required.", nameof(caseId));
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
        string Behavior,
        string Revision,
        string CaseId);
}
