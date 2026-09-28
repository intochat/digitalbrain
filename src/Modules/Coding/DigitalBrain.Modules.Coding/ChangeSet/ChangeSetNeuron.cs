using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.Roslyn;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Coding;

[GrainType("changeset")]
internal sealed class ChangeSetNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ChangeSetState> state,
    IOptions<CodingModuleOptions> options)
    : Neuron, IChangeSet
{
    // Orleans materializes an absent state as an instance with null collections, so normalize here.
    private ChangeSetState Current => state.State is { } stored
        ? stored with { Edits = stored.Edits ?? [], Diagnostics = stored.Diagnostics ?? [], Files = stored.Files ?? [] }
        : ChangeSetState.Empty;

    public async Task<ChangeSetReceipt> Propose(ProposeEdit request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RejectWhenClosed();
        if (request.Edit is null)
        {
            throw new ArgumentException("Provide one edit: kind plus the fields that kind needs.", nameof(request));
        }

        var current = Current;
        if (request.ExpectedVersion is { } expected && expected != current.Edits.Count)
        {
            throw new InvalidOperationException(
                $"expected version {expected} but the change set has {current.Edits.Count} edits. Read the change set and retry with the number of edits it reports.");
        }

        var next = current with
        {
            Status = ChangeSetStatus.Draft,
            Edits = [.. current.Edits, request.Edit],
            Diagnostics = [],
            Diff = null,
            Detail = null,
            Revision = current.Revision + 1,
        };
        await PersistAsync(next);
        return Receipt(next);
    }

    public async Task<ChangeSetReceipt> Check()
    {
        RejectWhenClosed();
        RejectWhenEmpty();
        var current = Current;
        var editDeadline = options.Value.EditDeadline;
        using var bounded = new CancellationTokenSource(editDeadline);
        ChangeSetState next;
        try
        {
            var outcome = await Roslyn().CheckEdits(current.Edits, bounded.Token);
            next = current with
            {
                Status = outcome.HasErrors ? ChangeSetStatus.Draft : ChangeSetStatus.Checked,
                Diagnostics = outcome.Diagnostics,
                Diff = outcome.Diff,
                Detail = outcome.Detail,
                Revision = current.Revision + 1,
            };
        }
        catch (OperationCanceledException)
        {
            next = Refused(current, $"the check did not finish within {editDeadline.TotalSeconds:0}s");
        }
#pragma warning disable CA1031 // any Roslyn or file-system failure must settle as advice, never leave the change set unsettled
        catch (Exception error) when (error is not OperationCanceledException)
#pragma warning restore CA1031
        {
            // The workspace is not ready, refused the snapshot, or Roslyn itself failed; the detail is the advice.
            next = Refused(current, error.Message);
        }

        await PersistAsync(next);
        return Receipt(next);
    }

    public async Task<ChangeSetReceipt> Commit(CommitChangeSet request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RejectWhenClosed();
        RejectWhenEmpty();
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("Say what the change does in one line.", nameof(request));
        }

        var current = Current;
        var editDeadline = options.Value.EditDeadline;
        // Only the edit work is bounded: once the writes begin they run to completion, so a deadline can
        // never abandon a commit halfway through writing files.
        using var bounded = new CancellationTokenSource(editDeadline);
        EditCommit? applied = null;
        ChangeSetState next;
        try
        {
            applied = await Roslyn().CommitEdits(current.Edits, bounded.Token);
            next = applied.HasErrors
                ? Refused(current, applied.Detail ?? "the change set has errors", applied)
                : current with
                {
                    Status = ChangeSetStatus.Committed,
                    Diagnostics = applied.Diagnostics,
                    Diff = applied.Diff,
                    Detail = null,
                    Files = applied.WrittenPaths,
                    Generation = applied.SnapshotVersion,
                    Revision = current.Revision + 1,
                };
        }
        catch (OperationCanceledException)
        {
            next = Refused(current, $"the commit did not finish within {editDeadline.TotalSeconds:0}s", applied);
        }
#pragma warning disable CA1031 // any Roslyn or file-system failure must settle as advice, never leave the change set unsettled
        catch (Exception error) when (error is not OperationCanceledException)
#pragma warning restore CA1031
        {
            next = Refused(current, error.Message + " Files may already have been written; read the change set and check the tree.", applied);
        }

        await PersistAsync(next);
        return Receipt(next);
    }

    public async Task<ChangeSetReceipt> Discard()
    {
        RejectWhenClosed();
        var current = Current;
        var next = current with { Status = ChangeSetStatus.Discarded, Detail = null, Revision = current.Revision + 1 };
        await PersistAsync(next);
        return Receipt(next);
    }

    [ReadOnly]
    public Task<ChangeSetSnapshot> Read()
    {
        var current = Current;
        return Task.FromResult(new ChangeSetSnapshot(current.Status, current.Edits, current.Diagnostics, current.Diff,
            current.Generation, current.Detail, current.Files, current.Revision));
    }

    private async Task PersistAsync(ChangeSetState next)
    {
        state.State = next;
        await state.WriteStateAsync();
        await PublishAsync(new ChangeSetChanged(this.GetPrimaryKeyString(), next.Status, next.Edits.Count, next.Revision));
    }

    private IRoslyn Roslyn() => GrainFactory.GetGrain<IRoslyn>(options.Value.WorkspaceKey);

    private static ChangeSetState Refused(ChangeSetState current, string detail, EditCheck? applied = null)
        => current with
        {
            Status = ChangeSetStatus.Draft,
            Diagnostics = applied?.Diagnostics ?? current.Diagnostics,
            Diff = applied?.Diff ?? current.Diff,
            Detail = detail,
            Revision = current.Revision + 1,
        };

    private static ChangeSetState Refused(ChangeSetState current, string detail, EditCommit? applied)
        => Refused(current, detail, applied is null ? null : new EditCheck(applied.Diagnostics, applied.Diff, null, applied.Detail, applied.HasErrors));

    private ChangeSetReceipt Receipt(ChangeSetState current) => new(this.GetPrimaryKeyString(), current.Edits.Count, current.Status);

    private void RejectWhenClosed()
    {
        if (Current.Status is ChangeSetStatus.Committed or ChangeSetStatus.Discarded)
        {
            var status = Current.Status.ToString().ToLowerInvariant();
            throw new InvalidOperationException(
                $"the change set '{this.GetPrimaryKeyString()}' is {status}; start a new change set with a different id.");
        }
    }

    private void RejectWhenEmpty()
    {
        if (Current.Edits.Count == 0)
        {
            throw new InvalidOperationException("Propose at least one edit first.");
        }
    }
}