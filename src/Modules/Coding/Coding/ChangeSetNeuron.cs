using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Coding;

[GrainType(CodingVocabulary.ChangeSetType)]
internal sealed class ChangeSetNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<ChangeSetState>> state,
    SolutionWorkspace workspace,
    ChangeSetEditor editor)
    : Neuron<ChangeSetState>(runtime, state), IChangeSet
{
    private ChangeSetState Current => State ?? ChangeSetState.Empty;

    public Task<Accepted<ChangeSetReceipt>> Propose(ProposeEdit command) => ExecuteCommandAsync(
        new("changeset", "propose"), command, CodingJson.Default.ProposeEdit, CodingJson.Default.AcceptedChangeSetReceipt, arguments =>
        {
            RejectWhenClosed(arguments.Id);
            if (arguments.Edit is null)
            {
                throw new CommandRejectedException(arguments.Id, "edit is missing", "Provide one edit: kind plus the fields that kind needs.");
            }

            if (arguments.ExpectedVersion is { } expected && expected != Current.Edits.Count)
            {
                throw new CommandRejectedException(arguments.Id, "stale expected version",
                    $"expected version {expected} but the change set has {Current.Edits.Count} edits. Read the change set and retry with the number of edits it reports.");
            }

            var work = Schedule(Signal.FromJson(CodingVocabulary.ChangeSetProposing, arguments.Edit, CodingJson.Default.EditRequest));
            return new Accepted<ChangeSetReceipt>(Receipt(Current.Edits.Count + 1), work);
        });

    public Task<Accepted<ChangeSetReceipt>> Check(CheckChangeSet command) => ExecuteCommandAsync(
        new("changeset", "check"), command, CodingJson.Default.CheckChangeSet, CodingJson.Default.AcceptedChangeSetReceipt, arguments =>
        {
            RejectWhenClosed(arguments.Id);
            RejectWhenEmpty(arguments.Id);
            var work = Schedule(Signal.Create(CodingVocabulary.ChangeSetChecking, "{}"));
            return new Accepted<ChangeSetReceipt>(Receipt(Current.Edits.Count), work);
        });

    public Task<Accepted<ChangeSetReceipt>> Commit(CommitChangeSet command) => ExecuteCommandAsync(
        new("changeset", "commit"), command, CodingJson.Default.CommitChangeSet, CodingJson.Default.AcceptedChangeSetReceipt, arguments =>
        {
            RejectWhenClosed(arguments.Id);
            RejectWhenEmpty(arguments.Id);
            if (string.IsNullOrWhiteSpace(arguments.Message))
            {
                throw new CommandRejectedException(arguments.Id, "message is blank", "Say what the change does in one line.");
            }

            var work = Schedule(Signal.FromJson(CodingVocabulary.ChangeSetCommitting, new CommittingBody(arguments.Message), CodingJson.Default.CommittingBody));
            return new Accepted<ChangeSetReceipt>(Receipt(Current.Edits.Count), work);
        });

    public Task<Accepted<ChangeSetReceipt>> Discard(DiscardChangeSet command) => ExecuteCommandAsync(
        new("changeset", "discard"), command, CodingJson.Default.DiscardChangeSet, CodingJson.Default.AcceptedChangeSetReceipt, arguments =>
        {
            RejectWhenClosed(arguments.Id);
            var work = Schedule(Signal.Create(CodingVocabulary.ChangeSetDiscarding, "{}"));
            return new Accepted<ChangeSetReceipt>(Receipt(Current.Edits.Count), work);
        });

    [ReadOnly]
    public Task<ChangeSetSnapshot> Read()
    {
        var current = Current;
        return Task.FromResult(new ChangeSetSnapshot(current.Status, current.Edits, current.Diagnostics, current.Diff, current.Generation, current.Detail, current.Files, current.Revision));
    }

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var current = Current;
        switch (delivery.Signal.Type)
        {
            case CodingVocabulary.ChangeSetProposing:
                {
                    if (Body(delivery, CodingJson.Default.EditRequest) is not { } edit)
                    {
                        return;
                    }

                    await SaveAsync(current with { Status = ChangeSetStatus.Draft, Edits = [.. current.Edits, edit], Diagnostics = [], Diff = null, Detail = null, Revision = current.Revision + 1 }, cancellationToken).ConfigureAwait(true);
                    break;
                }
            case CodingVocabulary.ChangeSetChecking:
                {
                    var editDeadline = EditDeadline;
                    using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    bounded.CancelAfter(editDeadline);
                    ChangeSetState next;
                    try
                    {
                        var outcome = await workspace.QueryAsync((solution, token) => editor.ApplyAsync(solution, current.Edits, token), bounded.Token).ConfigureAwait(true);
                        next = current with
                        {
                            Status = outcome.HasErrors ? ChangeSetStatus.Draft : ChangeSetStatus.Checked,
                            Diagnostics = outcome.Diagnostics,
                            Diff = outcome.Diff,
                            Detail = outcome.Detail,
                            Revision = current.Revision + 1,
                        };
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        next = Refused(current, $"the check did not finish within {editDeadline.TotalSeconds:0}s");
                    }
#pragma warning disable CA1031 // any Roslyn or file-system failure must settle as advice, never leave the change set unsettled and retrying
                    catch (Exception error) when (error is not OperationCanceledException)
#pragma warning restore CA1031
                    {
                        // The workspace is not ready, refused the snapshot, or Roslyn itself failed; the detail is the advice.
                        next = Refused(current, error.Message);
                    }

                    await SaveAsync(next, cancellationToken).ConfigureAwait(true);
                    break;
                }
            case CodingVocabulary.ChangeSetCommitting:
                {
                    var editDeadline = EditDeadline;
                    // Only the edit work is bounded: once the writes begin they run under the reaction's own
                    // token, so a deadline can never abandon a commit halfway through writing files.
                    using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    bounded.CancelAfter(editDeadline);
                    EditOutcome? applied = null;
                    ChangeSetState next;
                    try
                    {
                        var committed = await workspace.CommitAsync(async (solution, _) =>
                        {
                            applied = await editor.ApplyAsync(solution, current.Edits, bounded.Token).ConfigureAwait(true);
                            return applied.HasErrors
                                ? throw new InvalidOperationException(applied.Detail ?? "the change set has errors")
                                : applied.Changed;
                        }, cancellationToken).ConfigureAwait(true);
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
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        next = Refused(current, $"the commit did not finish within {editDeadline.TotalSeconds:0}s", applied);
                    }
#pragma warning disable CA1031 // any Roslyn or file-system failure must settle as advice, never leave the change set unsettled and retrying
                    catch (Exception error) when (error is not OperationCanceledException)
#pragma warning restore CA1031
                    {
                        // A change set with errors is refused before anything is written; any other failure
                        // can have come after TryApplyChanges, which writes the changed documents itself.
                        next = Refused(current, applied is { HasErrors: true }
                            ? error.Message
                            : error.Message + " Files may already have been written; read the change set and check the tree.", applied);
                    }

                    await SaveAsync(next, cancellationToken).ConfigureAwait(true);
                    break;
                }
            case CodingVocabulary.ChangeSetDiscarding:
                await SaveAsync(current with { Status = ChangeSetStatus.Discarded, Detail = null, Revision = current.Revision + 1 }, cancellationToken).ConfigureAwait(true);
                break;
            default:
                return;
        }
    }

    private TimeSpan EditDeadline => (ServiceProvider.GetService<CodingToolOptions>() ?? CodingToolOptions.Default).EditDeadline;

    private static ChangeSetState Refused(ChangeSetState current, string detail, EditOutcome? applied = null)
        => current with
        {
            Status = ChangeSetStatus.Draft,
            Diagnostics = applied?.Diagnostics ?? current.Diagnostics,
            Diff = applied?.Diff ?? current.Diff,
            Detail = detail,
            Revision = current.Revision + 1,
        };

    private ChangeSetReceipt Receipt(int editCount) => new(Name, editCount, Current.Status);

    private void RejectWhenClosed(CommandId id)
    {
        if (Current.Status is ChangeSetStatus.Committed or ChangeSetStatus.Discarded)
        {
            var status = Current.Status.ToString().ToLowerInvariant();
            throw new CommandRejectedException(id, "change set closed",
                $"the change set '{Name}' is {status}; start a new change set with a different id, for example '{Name}-2'.");
        }
    }

    private void RejectWhenEmpty(CommandId id)
    {
        if (Current.Edits.Count == 0)
        {
            throw new CommandRejectedException(id, "no edits", "Propose at least one edit first.");
        }
    }
}
