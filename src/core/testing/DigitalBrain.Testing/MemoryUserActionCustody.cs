using System.Collections.Concurrent;
using System.Security.Cryptography;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Tasks;

namespace DigitalBrain.Testing;

public sealed class MemoryUserActionCustody(TimeProvider time) : IUserActionCustody
{
    private readonly ConcurrentDictionary<Guid, StoredAction> _entries = new();
    private readonly ConcurrentDictionary<Guid, IssuedUserAction> _issuedByEpoch = new();

    public ValueTask<IssuedUserAction> IssueAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        NeuronId moduleNeuron,
        string moduleId,
        string displayText,
        ReadOnlyMemory<byte> actionMaterial,
        long parkRevision,
        TimeSpan lifetime,
        NeuronId completer,
        Guid actionEpoch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayText);
        ArgumentNullException.ThrowIfNull(time);

        if (owner == default)
        {
            throw new ArgumentException("Owner is required.", nameof(owner));
        }

        if (task == default || task.Owner != owner)
        {
            throw new ArgumentException("Task identity is required.", nameof(task));
        }

        if (attempt.Value == Guid.Empty)
        {
            throw new ArgumentException("Attempt identity is required.", nameof(attempt));
        }

        if (moduleNeuron == default)
        {
            throw new ArgumentException("Module identity is required.", nameof(moduleNeuron));
        }

        if (actionMaterial.IsEmpty)
        {
            throw new ArgumentException("Action material is required.", nameof(actionMaterial));
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Lifetime must be positive.");
        }

        if (completer == default || completer.Owner != owner)
        {
            throw new ArgumentException("Completer identity is required.", nameof(completer));
        }

        if (actionEpoch == Guid.Empty)
        {
            throw new ArgumentException("Action epoch is required.", nameof(actionEpoch));
        }

        var trimmedModuleId = moduleId.Trim();
        var trimmedDisplay = displayText.Trim();
        var payload = ModuleUserActionBoundary.SerializeCustodyMaterial(
            actionEpoch,
            moduleNeuron,
            trimmedModuleId,
            parkRevision,
            actionMaterial,
            completer,
            trimmedDisplay);

        if (_issuedByEpoch.TryGetValue(actionEpoch, out var existingIssued))
        {
            return ValueTask.FromResult(
                RequireExactEpochReissue(
                    existingIssued,
                    owner,
                    task,
                    attempt,
                    moduleNeuron,
                    trimmedModuleId,
                    trimmedDisplay,
                    payload,
                    parkRevision,
                    completer,
                    actionEpoch));
        }

        var now = time.GetUtcNow();
        var expiresAt = now + lifetime;
        var reference = new ProtectedPayloadReference(actionEpoch, expiresAt);

        _entries[reference.Id] = new StoredAction(
            owner,
            task,
            attempt,
            moduleNeuron,
            trimmedModuleId,
            actionEpoch,
            parkRevision,
            expiresAt,
            payload,
            completer,
            trimmedDisplay);

        var requirement = ModuleUserActionBoundary.Create(
            task,
            attempt,
            moduleNeuron,
            trimmedModuleId,
            trimmedDisplay,
            reference,
            actionEpoch,
            parkRevision,
            expiresAt,
            completer);

        var issued = new IssuedUserAction(requirement);
        if (!_issuedByEpoch.TryAdd(actionEpoch, issued))
        {
            return ValueTask.FromResult(
                RequireExactEpochReissue(
                    _issuedByEpoch[actionEpoch],
                    owner,
                    task,
                    attempt,
                    moduleNeuron,
                    trimmedModuleId,
                    trimmedDisplay,
                    payload,
                    parkRevision,
                    completer,
                    actionEpoch));
        }

        return ValueTask.FromResult(issued);
    }

    private IssuedUserAction RequireExactEpochReissue(
        IssuedUserAction existingIssued,
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        NeuronId moduleNeuron,
        string moduleId,
        string displayText,
        byte[] payload,
        long parkRevision,
        NeuronId completer,
        Guid actionEpoch)
    {
        var existing = existingIssued.Requirement;
        if (existing.Task != task
            || existing.Attempt != attempt
            || existing.Module != moduleNeuron
            || !string.Equals(existing.ModuleId, moduleId, StringComparison.Ordinal)
            || existing.ParkRevision != parkRevision
            || existing.Completer != completer
            || existing.ActionEpoch != actionEpoch
            || !string.Equals(existing.DisplayText, displayText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"User-action epoch '{actionEpoch:N}' is already issued for a different binding.");
        }

        if (existing.ExpiresAt <= time.GetUtcNow())
        {
            throw new InvalidOperationException(
                $"User-action epoch '{actionEpoch:N}' has expired.");
        }

        if (!_entries.TryGetValue(existing.ActionReference.Id, out var stored)
            || stored.Owner != owner
            || stored.Task != task
            || stored.Attempt != attempt
            || stored.Module != moduleNeuron
            || !string.Equals(stored.ModuleId, moduleId, StringComparison.Ordinal)
            || stored.ParkRevision != parkRevision
            || stored.Completer != completer
            || stored.Payload.Length != payload.Length
            || !CryptographicOperations.FixedTimeEquals(stored.Payload, payload))
        {
            throw new InvalidOperationException(
                $"User-action epoch '{actionEpoch:N}' is already issued for a different binding.");
        }

        return existingIssued;
    }

    public bool TryLoadActionMaterial(
        ProtectedPayloadReference actionReference,
        out byte[] actionMaterial)
    {
        actionMaterial = [];
        if (!_entries.TryGetValue(actionReference.Id, out var stored))
        {
            return false;
        }

        var material = ModuleUserActionBoundary.DeserializeCustodyMaterial(stored.Payload);
        actionMaterial = Convert.FromBase64String(material.ActionMaterialBase64);
        return true;
    }

    private sealed record StoredAction(
        OwnerId Owner,
        NeuronId Task,
        AttemptId Attempt,
        NeuronId Module,
        string ModuleId,
        Guid ActionEpoch,
        long ParkRevision,
        DateTimeOffset ExpiresAt,
        byte[] Payload,
        NeuronId Completer,
        string DisplayText);
}
