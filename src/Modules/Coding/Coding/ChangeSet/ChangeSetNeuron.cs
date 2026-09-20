using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Coding;

[GrainType("changeset")]
internal sealed class ChangeSetNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ChangeSetState> state,
    SolutionWorkspace workspace,
    ChangeSetEditor editor,
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
            var outcome = await workspace.QueryAsync((solution, token) => editor.ApplyAsync(solution, current.Edits, token), bounded.Token);
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
        EditOutcome? applied = null;
        ChangeSetState next;
        try
        {
            var committed = await workspace.CommitAsync(async (solution, _) =>
            {
                applied = await editor.ApplyAsync(solution, current.Edits, bounded.Token);
                return applied.HasErrors
                    ? throw new InvalidOperationException(applied.Detail ?? "the change set has errors")
                    : applied.Changed;
            }, CancellationToken.None);
            next = current with
            {
                Status = ChangeSetStatus.Committed,
                Diagnostics = applied!.Diagnostics,
                Diff = applied.Diff,
                Detail = null,
                Files = committed.WrittenPaths,
                Generation = committed.SnapshotVersion,
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
            // A change set with errors is refused before anything is written; any other failure can have
            // come after TryApplyChanges, which writes the changed documents itself.
            next = Refused(current, applied is { HasErrors: true }
                ? error.Message
                : error.Message + " Files may already have been written; read the change set and check the tree.", applied);
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

    private static ChangeSetState Refused(ChangeSetState current, string detail, EditOutcome? applied = null)
        => current with
        {
            Status = ChangeSetStatus.Draft,
            Diagnostics = applied?.Diagnostics ?? current.Diagnostics,
            Diff = applied?.Diff ?? current.Diff,
            Detail = detail,
            Revision = current.Revision + 1,
        };

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