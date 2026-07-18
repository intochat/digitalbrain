using Brain.Contracts;

namespace Brain.Kernel;

public sealed class ReactiveNeuronPipeline(IReactiveStore store, int maxCausalDepth = 8)
{
    public const string DomainStateKey = "domain-state";
    public const string UiRevisionKey = "ui-revision";
    public const string RevisionKey = "revision";
    public const string ReactionCountKey = "reaction-count";

    public long CurrentRevision =>
        store.Flags.TryGetValue(RevisionKey, out var value) && long.TryParse(value, out var revision) ? revision : 0;

    public long UiRevision =>
        store.Flags.TryGetValue(UiRevisionKey, out var value) && long.TryParse(value, out var revision) ? revision : 0;

    public string DomainState =>
        store.Domain.TryGetValue(DomainStateKey, out var value) ? value : string.Empty;

    public int ReactionCount =>
        store.Flags.TryGetValue(ReactionCountKey, out var value) && int.TryParse(value, out var count) ? count : 0;

    public async Task<CommandReceipt> ExecuteCommandAsync<T>(
        CommandSynapse<T> command,
        CommandHandlerAsync<T> handler)
    {
        var commandKey = command.Metadata.CommandId.ToString("N");
        if (store.Receipts.TryGetValue(commandKey, out var existing))
            return existing;

        try
        {
            EnsureCausalRules(command.Metadata);

            CommandReceipt? committedReceipt = null;
            await handler(command.Payload, async commit =>
            {
                if (committedReceipt is not null)
                    throw new InvalidOperationException("command already committed");

                ApplyCommit(commit);
                IncrementReaction();
                committedReceipt = new CommandReceipt(
                    command.Metadata.CommandId,
                    CommandReceiptStatus.Accepted,
                    CurrentRevision,
                    null,
                    null);
                store.Receipts[commandKey] = committedReceipt;
                await store.CommitAsync();
            });

            if (committedReceipt is null)
                throw new InvalidOperationException("command handler must commit before completing");

            return committedReceipt;
        }
        catch (BrainException ex) when (ex.Code == BrainErrors.JournalCommitFailed)
        {
            throw;
        }
        catch (BrainException ex)
        {
            RecordFailure(ex.Code, Sanitize(ex.Message), command.Metadata.CommandId, null);
            try
            {
                await store.CommitAsync();
            }
            catch (BrainException commitEx) when (commitEx.Code == BrainErrors.JournalCommitFailed)
            {
                throw;
            }

            throw;
        }
        catch (Exception ex) when (ex is not BrainException)
        {
            RecordFailure(BrainErrors.FailureSanitized, Sanitize(ex.Message), command.Metadata.CommandId, null);
            try
            {
                await store.CommitAsync();
            }
            catch (BrainException commitEx) when (commitEx.Code == BrainErrors.JournalCommitFailed)
            {
                throw;
            }

            throw new BrainException(BrainErrors.FailureSanitized, Sanitize(ex.Message));
        }
    }

    public async Task HandleEventAsync<T>(EventSynapse<T> @event, EventHandlerAsync<T> handler)
    {
        var eventKey = @event.Metadata.EventId.ToString("N");
        if (store.ProcessedEvents.ContainsKey(eventKey))
            return;

        try
        {
            EnsureCausalRules(@event.Metadata);
            EnsureSourceSequence(@event.Metadata);

            var committed = false;
            await handler(@event.Payload, async commit =>
            {
                if (committed)
                    throw new InvalidOperationException("event already committed");

                store.ProcessedEvents[eventKey] = 1;
                ApplySourceSequence(@event.Metadata);
                ApplyCommit(commit);
                IncrementReaction();
                committed = true;
                await store.CommitAsync();
            });

            if (!committed)
                throw new InvalidOperationException("event handler must commit before completing");
        }
        catch (BrainException ex) when (ex.Code == BrainErrors.JournalCommitFailed)
        {
            throw;
        }
        catch (BrainException ex)
        {
            if (ex.Code is BrainErrors.CausalLoop or BrainErrors.CausalDepthExceeded)
                store.RejectedCausation[@event.Metadata.CausationId.ToString("N")] = 1;

            RecordFailure(ex.Code, Sanitize(ex.Message), @event.Metadata.CommandId, @event.Metadata.EventId);
            try
            {
                await store.CommitAsync();
            }
            catch (BrainException commitEx) when (commitEx.Code == BrainErrors.JournalCommitFailed)
            {
                throw;
            }

            throw;
        }
        catch (Exception ex) when (ex is not BrainException)
        {
            RecordFailure(BrainErrors.FailureSanitized, Sanitize(ex.Message), @event.Metadata.CommandId, @event.Metadata.EventId);
            try
            {
                await store.CommitAsync();
            }
            catch (BrainException commitEx) when (commitEx.Code == BrainErrors.JournalCommitFailed)
            {
                throw;
            }

            throw new BrainException(BrainErrors.FailureSanitized, Sanitize(ex.Message));
        }
    }

    public void EnsureExpectedUiRevision(long expectedRevision)
    {
        if (expectedRevision != UiRevision)
            throw new BrainException(BrainErrors.RevisionConflict, $"expected {expectedRevision}, actual {UiRevision}");
    }

    public void IncrementReaction()
    {
        store.Flags[ReactionCountKey] = (ReactionCount + 1).ToString();
    }

    private void EnsureCausalRules(SynapseMetadata metadata)
    {
        if (metadata.CausalDepth > maxCausalDepth)
            throw new BrainException(BrainErrors.CausalDepthExceeded, $"causal depth {metadata.CausalDepth} exceeds {maxCausalDepth}");

        if (metadata.CausalDepth > 0 && metadata.EventId == metadata.CausationId)
            throw new BrainException(BrainErrors.CausalLoop, "event id equals causation id");

        if (store.RejectedCausation.ContainsKey(metadata.CausationId.ToString("N")))
            throw new BrainException(BrainErrors.CausalLoop, "causation previously rejected");
    }

    private void EnsureSourceSequence(SynapseMetadata metadata)
    {
        var sourceKey = metadata.Source.ToGrainKey();
        if (!store.SourceSequences.TryGetValue(sourceKey, out var last))
            return;

        if (metadata.SourceSequence != last + 1)
            throw new BrainException(BrainErrors.OutOfOrderSource, $"expected {last + 1}, received {metadata.SourceSequence}");
    }

    private void ApplySourceSequence(SynapseMetadata metadata)
    {
        store.SourceSequences[metadata.Source.ToGrainKey()] = metadata.SourceSequence;
    }

    private void ApplyCommit(ReactiveCommit commit)
    {
        store.Domain[DomainStateKey] = commit.DomainState;
        store.Flags[UiRevisionKey] = commit.UiRevision.ToString();
        store.Flags[RevisionKey] = (CurrentRevision + 1).ToString();

        foreach (var intent in commit.Outbox)
            store.Outbox.Add(intent);
    }

    private void RecordFailure(string code, string message, Guid? commandId, Guid? eventId)
    {
        store.Failures.Add(new SanitizedFailure(
            FailureId: Guid.NewGuid(),
            Code: code,
            Message: message,
            OccurredAt: DateTimeOffset.UtcNow,
            CommandId: commandId,
            EventId: eventId));
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "failure";

        var trimmed = message.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200];
    }
}
