using Brain.Abstractions.Activities;
using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace Brain.Core.Journaling;

public sealed class DurableBrainActivityGrain(
    [FromKeyedServices("records")] IDurableList<BrainJournalRecord> records,
    [FromKeyedServices("record-sequences")] IDurableDictionary<Guid, long> recordSequences,
    [FromKeyedServices("activity")] IDurableValue<BrainActivitySnapshot?> activity,
    [FromKeyedServices("invocation")] IDurableValue<BrainOperationInvocation?> storedInvocation)
    : DurableGrain, IBrainActivityGrain
{
    public async Task<BrainActivityReceipt> StartAsync(
        Guid activityId,
        BrainOperationInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ValidateIdentity(invocation.WorkspaceId, activityId);

        if (activity.Value is { } existing)
        {
            if (existing.ActivityId != activityId
                || !string.Equals(existing.OperationId, invocation.OperationId, StringComparison.Ordinal)
                || !string.Equals(existing.WorkspaceId, invocation.WorkspaceId, StringComparison.Ordinal)
                || storedInvocation.Value != invocation)
            {
                throw new InvalidOperationException("An activity grain cannot be reused for another operation.");
            }

            return new BrainActivityReceipt(existing.ActivityId, existing.OperationId);
        }

        activity.Value = new BrainActivitySnapshot(
            activityId,
            invocation.OperationId,
            invocation.WorkspaceId,
            ActivityStatus.Accepted,
            0,
            null,
            null);
        storedInvocation.Value = invocation;
        await WriteStateAsync();
        return new BrainActivityReceipt(activityId, invocation.OperationId);
    }

    public async Task<BrainJournalRecord> AppendAsync(BrainJournalWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        ValidateIdentity(write.WorkspaceId, write.ActivityId);
        if (activity.Value is null)
        {
            throw new InvalidOperationException("The activity must be started before its journal can be appended.");
        }

        if (recordSequences.TryGetValue(write.RecordId, out var existingSequence))
        {
            return records[checked((int)existingSequence - 1)];
        }

        var sequence = records.Count + 1L;
        var record = write.WithSequence(sequence);
        records.Add(record);
        recordSequences.Add(record.RecordId, sequence);
        activity.Value = activity.Value with
        {
            Status = ActivityStatus.Running,
            Sequence = sequence,
        };
        await WriteStateAsync();
        return record;
    }

    public Task<BrainJournalPage> ReadJournalAsync(string workspaceId, long afterSequence, int take)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        if (afterSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(afterSequence));
        }
        if (take is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        var identity = ParseIdentity();
        if (!string.Equals(identity.WorkspaceId, workspaceId, StringComparison.Ordinal))
        {
            return Task.FromResult(new BrainJournalPage(
                workspaceId,
                identity.ActivityId,
                afterSequence,
                afterSequence,
                [],
                false));
        }

        var pageRecords = records
            .Where(record => record.Sequence > afterSequence)
            .Take(take)
            .ToArray();
        var lastSequence = pageRecords.Length == 0 ? afterSequence : pageRecords[^1].Sequence;
        var hasMore = records.Any(record => record.Sequence > lastSequence);
        return Task.FromResult(new BrainJournalPage(
            workspaceId,
            identity.ActivityId,
            afterSequence,
            lastSequence,
            pageRecords,
            hasMore));
    }

    public Task<BrainActivitySnapshot?> GetAsync(string workspaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        return Task.FromResult(
            activity.Value is { } value
                && string.Equals(value.WorkspaceId, workspaceId, StringComparison.Ordinal)
                    ? value
                    : null);
    }

    public async Task CompleteAsync(string workspaceId, string resultJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultJson);
        var current = RequireActivity(workspaceId);
        activity.Value = current with
        {
            Status = ActivityStatus.Completed,
            ResultJson = resultJson,
            Problem = null,
        };
        await WriteStateAsync();
    }

    public async Task FailAsync(string workspaceId, string problem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(problem);
        var current = RequireActivity(workspaceId);
        activity.Value = current with
        {
            Status = ActivityStatus.Failed,
            ResultJson = null,
            Problem = problem,
        };
        await WriteStateAsync();
    }

    private BrainActivitySnapshot RequireActivity(string workspaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        if (activity.Value is not { } current
            || !string.Equals(current.WorkspaceId, workspaceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The addressed activity does not exist in this workspace.");
        }
        return current;
    }

    private void ValidateIdentity(string workspaceId, Guid activityId)
    {
        var identity = ParseIdentity();
        if (!string.Equals(identity.WorkspaceId, workspaceId, StringComparison.Ordinal)
            || identity.ActivityId != activityId)
        {
            throw new InvalidOperationException("The activity request does not match the addressed workspace activity.");
        }
    }

    private (string WorkspaceId, Guid ActivityId) ParseIdentity()
    {
        var key = this.GetPrimaryKeyString();
        var separator = key.LastIndexOf('/');
        if (separator <= 0 || !Guid.TryParseExact(key[(separator + 1)..], "n", out var activityId))
        {
            throw new InvalidOperationException("Activity grain keys must be '<workspace>/<activity-n>'.");
        }

        return (key[..separator], activityId);
    }
}
