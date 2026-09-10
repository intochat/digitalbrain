using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

internal sealed class CommandExecution(CommandJournal journal, CommandDedup dedup, TimeProvider clock,
    ICommandCrashPoint? crashPoint)
{
    internal async Task<TResult> RunAsync<TArguments, TResult>(
        ICommandHost host, CommandDescriptor command, TArguments arguments,
        JsonTypeInfo<TArguments> argumentsJson, JsonTypeInfo<TResult> resultJson,
        Func<TArguments, TResult> execute) where TArguments : Command
    {
        var caller = CallerContext.Current();
        var cause = (host.ReactionContext as DeliveryReaction)?.Delivery;
        var record = new CommandRecord(
            0, arguments.Id, 1, command.InterfaceAlias, command.MethodAlias, CommandPhase.Rejected,
            caller, cause?.CorrelationId ?? CallerContext.CurrentCorrelation() ?? CorrelationId.New(),
            cause?.SignalId ?? CallerContext.CurrentCausation(), null, null, null, clock.GetUtcNow());
        var argumentsText = JsonSerializer.Serialize(arguments, argumentsJson);
        var bytes = Encoding.UTF8.GetBytes(argumentsText);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var existing = dedup.Find(arguments.Id);
        record = record with { Incarnation = existing is null ? 1 : existing.Incarnation + 1 };
        if (bytes.Length > CommandLimits.MaxArgumentBytes)
        {
            return await RejectAsync<TResult>(host, record, hash, new CommandRejectedException(
                arguments.Id, "arguments over the 64 KB limit",
                $"Command arguments are {(bytes.Length + 1023) / 1024} KB; the limit is 64 KB. Pass a reference instead of the payload.")).ConfigureAwait(true);
        }

        var replay = await TryReplay(host, record, existing, hash, resultJson).ConfigureAwait(true);
        if (replay.Applies)
        {
            return replay.Result;
        }

        if (existing is null && dedup.IsFullOfUnresolved)
        {
            // A full unresolved dedup cannot admit an entry to remember this rejection.
            var error = new NeuronBusyException(
                $"Neuron '{host.Id}' already holds {CommandDedup.MaxResolved} unresolved commands. Retry after pending commands resolve.");
            journal.Append(record with { Error = TruncateError(error.Message) });
            await host.PersistAsync().ConfigureAwait(true);
            throw error;
        }

        if (!host.HasPendingRoom)
        {
            // Do not remember this transient rejection in dedup: pending capacity can free up.
            var error = new NeuronBusyException(
                $"Neuron '{host.Id}' already holds {PendingWork.MaxPending} pending signals. Retry after pending work finishes.");
            journal.Append(record with { Error = TruncateError(error.Message) });
            await host.PersistAsync().ConfigureAwait(true);
            throw error;
        }

        record = journal.Append(record with { Phase = CommandPhase.Attempted, ArgsJson = argumentsText });
        var outcome = new CommandOutcome(
            CommandPhase.Attempted, record.Incarnation, caller, command.InterfaceAlias, command.MethodAlias,
            hash, null, null, record.Sequence);
        dedup.Record(arguments.Id, outcome);
        await host.PersistAsync().ConfigureAwait(true);

        TResult result = default!;
        Exception? failure = null;
        try
        {
            IReadOnlyList<SignalId>? scheduledWork = null;
            var previous = host.ReactionContext;
            host.ReactionContext = new CommandReaction(arguments.Id, []);
            try
            {
                result = execute(arguments);
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                if (host.ReactionContext is CommandReaction { ScheduledWork.Count: > 0 } reaction)
                {
                    scheduledWork = reaction.ScheduledWork;
                }

                host.ReactionContext = previous;
            }

            string? resultText = null;
            string? errorText = null;
            if (failure is null)
            {
                try
                {
                    resultText = JsonSerializer.Serialize(result, resultJson);
                    if (Encoding.UTF8.GetByteCount(resultText) > CommandLimits.MaxResultBytes)
                    {
                        resultText = null;
                        errorText = "The result was omitted because it exceeds the 64 KB record limit.";
                    }
                }
                catch (Exception error)
                {
                    failure = error;
                }
            }

            if (failure is not null)
            {
                errorText = TruncateError(failure.Message);
            }

            AppendTerminalRecord(record, outcome, failure, resultText, errorText, scheduledWork);
            host.AdmitCommandWork();
            await host.PersistAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (error is not NeuronPersistenceException and not NeuronRecoveringException)
        {
            await host.DiscardStagedChangesAsync(error).ConfigureAwait(true);
            throw;
        }

        // Register after persistence so work that never committed cannot leave an orphan reminder row.
        await host.WakeCommandWorkAsync().ConfigureAwait(true);

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result;
    }

    private async Task<(bool Applies, TResult Result)> TryReplay<TResult>(
        ICommandHost host, CommandRecord record, CommandOutcome? existing, string hash, JsonTypeInfo<TResult> resultJson)
    {
        if (existing is null)
        {
            return (false, default!);
        }

        if (existing.MismatchAgainst(record.Caller, record.Interface, record.Method, hash) is { } mismatch)
        {
            return (true, await RejectAsync<TResult>(host, record, hash, new CommandRejectedException(
                record.Id, mismatch,
                $"Neuron '{host.Id}' refuses a command id reused with {mismatch}. Mint a new command id.")).ConfigureAwait(true));
        }

        switch (existing.Phase)
        {
            case CommandPhase.Completed:
                if (existing.ResultJson is { } storedResult)
                {
                    return (true, JsonSerializer.Deserialize(storedResult, resultJson)!);
                }

                throw new CommandOutcomeUnknownException(record.Id, existing.Error ?? $"Command '{record.Id}' has no recorded result.");
            case CommandPhase.Failed:
                throw new CommandFailedException(record.Id, existing.Error ?? $"Command '{record.Id}' failed.");
            case CommandPhase.Attempted:
                throw new CommandOutcomeUnknownException(record.Id,
                    $"Command '{record.Id}' on '{host.Id}' is still attempting; its outcome is unknown. Retry with the same id.");
        }

        // A stored Rejected or Unknown phase is not a replay: the caller goes on to attempt a fresh incarnation.
        return (false, default!);
    }

    private void AppendTerminalRecord(CommandRecord record, CommandOutcome outcome, Exception? failure,
        string? resultText, string? errorText, IReadOnlyList<SignalId>? scheduledWork)
    {
        var terminal = record with
        {
            Phase = failure is null ? CommandPhase.Completed : CommandPhase.Failed,
            ArgsJson = null,
            ResultJson = resultText,
            Error = errorText,
            At = clock.GetUtcNow(),
            ScheduledWork = scheduledWork,
        };
        crashPoint?.BeforeTerminalRecord(record.Id);
        terminal = journal.Append(terminal);
        dedup.Record(record.Id, outcome with
        {
            Phase = terminal.Phase,
            ResultJson = terminal.ResultJson,
            Error = terminal.Error,
            Sequence = terminal.Sequence,
        });
    }

    private async Task<TResult> RejectAsync<TResult>(ICommandHost host, CommandRecord record, string hash,
        CommandRejectedException error)
    {
        var existing = dedup.Find(error.Id);
        // The reason alone identifies a rejection because each new attempt replaces the outcome without a rejection.
        if (existing?.RepeatsRejection(error.Reason) == true)
        {
            throw new CommandRejectedException(error.Id, existing.Rejection!.Reason, existing.Rejection.Message);
        }

        record = journal.Append(record with { Error = TruncateError(error.Message) });
        var rejection = new CommandRejection(record.Incarnation, error.Reason, error.Message);
        dedup.Record(error.Id, existing is not null
            ? existing with { Rejection = rejection }
            : new CommandOutcome(CommandPhase.Rejected, record.Incarnation, record.Caller, record.Interface,
                record.Method, hash, null, null, record.Sequence, rejection));
        await host.PersistAsync().ConfigureAwait(true);
        throw error;
    }

    private static string TruncateError(string text)
    {
        var bytes = 0;
        var length = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > CommandLimits.MaxErrorBytes)
            {
                break;
            }

            bytes += rune.Utf8SequenceLength;
            length += rune.Utf16SequenceLength;
        }

        return text[..length];
    }
}
