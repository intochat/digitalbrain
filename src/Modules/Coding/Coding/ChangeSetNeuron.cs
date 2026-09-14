using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
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
        Descriptor("propose"), command, CodingJson.Default.ProposeEdit, CodingJson.Default.AcceptedChangeSetReceipt, arguments =>
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
        Descriptor("check"), command, CodingJson.Default.CheckChangeSet, CodingJson.Default.AcceptedChangeSetReceipt, arguments =>
        {
            RejectWhenClosed(arguments.Id);
            RejectWhenEmpty(arguments.Id);
            var work = Schedule(Signal.Create(CodingVocabulary.ChangeSetChecking, "{}"));
            return new Accepted<ChangeSetReceipt>(Receipt(Current.Edits.Count), work);
        });

    public Task<Accepted<ChangeSetReceipt>> Commit(CommitChangeSet command) => ExecuteCommandAsync(
        Descriptor("commit"), command, CodingJson.Default.CommitChangeSet, CodingJson.Default.AcceptedChangeSetReceipt, arguments =>
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
        Descriptor("discard"), command, CodingJson.Default.DiscardChangeSet, CodingJson.Default.AcceptedChangeSetReceipt, arguments =>
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
                    ChangeSetState next;
                    try
                    {
                        var outcome = await workspace.QueryAsync((solution, token) => editor.ApplyAsync(solution, current.Edits, token), cancellationToken).ConfigureAwait(true);
                        next = current with
                        {
                            Status = outcome.HasErrors ? ChangeSetStatus.Draft : ChangeSetStatus.Checked,
                            Diagnostics = outcome.Diagnostics,
                            Diff = outcome.Diff,
                            Detail = outcome.Detail,
                            Revision = current.Revision + 1,
                        };
                    }
                    catch (InvalidOperationException error)
                    {
                        // The workspace is not ready or refused the snapshot; the detail is the advice.
                        next = current with { Status = ChangeSetStatus.Draft, Detail = error.Message, Revision = current.Revision + 1 };
                    }

                    await SaveAsync(next, cancellationToken).ConfigureAwait(true);
                    break;
                }
            case CodingVocabulary.ChangeSetCommitting:
                {
                    EditOutcome? applied = null;
                    ChangeSetState next;
                    try
                    {
                        var committed = await workspace.CommitAsync(async (solution, token) =>
                        {
                            applied = await editor.ApplyAsync(solution, current.Edits, token).ConfigureAwait(true);
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
                    catch (InvalidOperationException error)
                    {
                        next = current with
                        {
                            Status = ChangeSetStatus.Draft,
                            Diagnostics = applied?.Diagnostics ?? current.Diagnostics,
                            Diff = applied?.Diff ?? current.Diff,
                            Detail = error.Message,
                            Revision = current.Revision + 1,
                        };
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

    private ChangeSetReceipt Receipt(int editCount) => new(Id.Name, editCount, Current.Status);

    private void RejectWhenClosed(CommandId id)
    {
        if (Current.Status is ChangeSetStatus.Committed or ChangeSetStatus.Discarded)
        {
            var status = Current.Status.ToString().ToLowerInvariant();
            throw new CommandRejectedException(id, "change set closed",
                $"the change set '{Id.Name}' is {status}; start a new change set with a different id, for example '{Id.Name}-2'.");
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
