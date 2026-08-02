using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors.Runtime;

internal sealed class GrainUserActionCustody(IBehaviorProtectedPayloadAccess payloads, TimeProvider time) : IUserActionCustody
{
    public async ValueTask<IssuedUserAction> IssueAsync(
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
        ArgumentNullException.ThrowIfNull(payloads);
        ArgumentNullException.ThrowIfNull(time);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayText);

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

        // Stable entry id = action epoch so custody reissue for the same binding is idempotent.
        var reference = await payloads
            .StoreAsync(owner, task, attempt, payload, lifetime, cancellationToken, stableEntryId: actionEpoch)
            .ConfigureAwait(false);

        var expiresAt = reference.ExpiresAt ?? time.GetUtcNow() + lifetime;
        if (reference.ExpiresAt is null)
        {
            reference = new ProtectedPayloadReference(reference.Id, expiresAt);
        }
        else
        {
            expiresAt = reference.ExpiresAt.Value;
        }

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

        return new IssuedUserAction(requirement);
    }
}
