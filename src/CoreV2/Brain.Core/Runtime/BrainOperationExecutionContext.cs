using System.Security.Cryptography;
using System.Text;
using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;

namespace Brain.Core.Runtime;

public sealed class BrainOperationExecutionContext(
    Guid activityId,
    BrainOperationInvocation invocation,
    IBrainActivityGrain activity,
    IGrainFactory grains)
{
    public Guid ActivityId { get; } = activityId;

    public BrainOperationInvocation Invocation { get; } = invocation;

    public IGrainFactory Grains { get; } = grains;

    public Guid DeterministicId(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var material = Encoding.UTF8.GetBytes($"{ActivityId:n}\0{label}");
        var hash = SHA256.HashData(material);
        var id = new Guid(hash.AsSpan(0, 16));
        return id == Guid.Empty ? new Guid(hash.AsSpan(16, 16)) : id;
    }

    public Task<BrainJournalRecord> JournalAsync(
        string label,
        string neuronId,
        BrainJournalDirection direction,
        string contractId,
        string outcome,
        string summary,
        int routeCount = 0,
        Guid? firingId = null,
        Guid? causeFiringId = null,
        Guid? synapseId = null,
        long? synapseRevision = null)
        => activity.AppendAsync(new BrainJournalWrite(
            DeterministicId($"record:{label}"),
            Invocation.WorkspaceId,
            ActivityId,
            Invocation.PrincipalId,
            neuronId,
            direction,
            contractId,
            firingId ?? DeterministicId($"firing:{label}"),
            causeFiringId,
            synapseId,
            synapseRevision,
            TimeProvider.System.GetUtcNow(),
            routeCount,
            outcome,
            summary));
}
