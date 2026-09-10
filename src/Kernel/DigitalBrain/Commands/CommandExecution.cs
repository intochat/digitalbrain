using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
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
            caller, cause?.CorrelationId ?? CorrelationId.New(), cause?.SignalId, null, null, null, clock.GetUtcNow());
        var argumentsText = JsonSerializer.Serialize(arguments, argumentsJson);
        var bytes = Encoding.UTF8.GetBytes(argumentsText);
        if (bytes.Length > CommandLimits.MaxArgumentBytes)
        {
            return await RejectAsync<TResult>(host, record, new CommandRejectedException(
                arguments.Id, "arguments over the 64 KB limit",
                $"Command arguments are {(bytes.Length + 1023) / 1024} KB; the limit is 64 KB. Pass a reference instead of the payload.")).ConfigureAwait(true);
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var existing = dedup.Find(arguments.Id);
        if (existing is not null)
        {
            record = record with { Incarnation = existing.Incarnation + 1 };
            if (existing.MismatchAgainst(caller, command.InterfaceAlias, command.MethodAlias, hash) is { } mismatch)
            {
                return await RejectAsync<TResult>(host, record, new CommandRejectedException(
                    arguments.Id, mismatch,
                    $"Neuron '{host.Id}' refuses a command id reused with {mismatch}. Mint a new command id.")).ConfigureAwait(true);
            }

            switch (existing.Phase)
            {
                case CommandPhase.Completed:
                    if (existing.ResultJson is { } storedResult)
                    {
                        return JsonSerializer.Deserialize(storedResult, resultJson)!;
                    }

                    throw new CommandOutcomeUnknownException(arguments.Id, existing.Error ?? $"Command '{arguments.Id}' has no recorded result.");
                case CommandPhase.Failed:
                    throw new CommandFailedException(arguments.Id, existing.Error ?? $"Command '{arguments.Id}' failed.");
                case CommandPhase.Attempted:
                    throw new CommandOutcomeUnknownException(arguments.Id,
                        $"Command '{arguments.Id}' on '{host.Id}' is still attempting; its outcome is unknown. Retry with the same id.");
            }
        }
        else if (dedup.IsFullOfUnresolved)
        {
            return await RejectAsync<TResult>(host, record, new NeuronBusyException(
                $"Neuron '{host.Id}' already holds {CommandDedup.MaxResolved} unresolved commands. Retry after pending commands resolve.")).ConfigureAwait(true);
        }

        record = journal.Append(record with { Phase = CommandPhase.Attempted, ArgsJson = argumentsText });
        var outcome = new CommandOutcome(
            CommandPhase.Attempted, record.Incarnation, caller, command.InterfaceAlias, command.MethodAlias,
            hash, null, null, record.Sequence);
        dedup.Record(arguments.Id, outcome);
        await host.PersistAsync().ConfigureAwait(true);

        var previous = host.ReactionContext;
        host.ReactionContext = new CommandReaction(arguments.Id, []);
        TResult result = default!;
        Exception? failure = null;
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

        var terminal = record with
        {
            Phase = failure is null ? CommandPhase.Completed : CommandPhase.Failed,
            ArgsJson = null,
            ResultJson = resultText,
            Error = errorText,
            At = clock.GetUtcNow(),
        };
        crashPoint?.BeforeTerminalRecord(arguments.Id);
        terminal = journal.Append(terminal);
        dedup.Record(arguments.Id, outcome with
        {
            Phase = terminal.Phase,
            ResultJson = terminal.ResultJson,
            Error = terminal.Error,
            Sequence = terminal.Sequence,
        });
        host.AdmitCommandWork();
        await host.PersistAsync().ConfigureAwait(true);
        // Register after persistence so work that never committed cannot leave an orphan reminder row.
        await host.WakeCommandWorkAsync().ConfigureAwait(true);

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result;
    }

    private async Task<TResult> RejectAsync<TResult>(ICommandHost host, CommandRecord record, Exception error)
    {
        journal.Append(record with { Error = TruncateError(error.Message) });
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
