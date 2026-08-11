using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Execution;

internal sealed class ExecutionCommandHandler
{
    private readonly ExecutionCanceller _canceller;
    private readonly ExecutionOperationResolver _operationResolver;
    private readonly ExecutionRuntime _runtime;
    private readonly ExecutionStarter _starter;

    internal ExecutionCommandHandler(ExecutionRuntime runtime, ExecutionDispatcher dispatcher)
    {
        _runtime = runtime;
        _starter = new ExecutionStarter(runtime, dispatcher);
        _canceller = new ExecutionCanceller(runtime, dispatcher);
        _operationResolver = new ExecutionOperationResolver(runtime, dispatcher);
    }

    internal async Task<ExecutionSnapshot> ApplyAsync(ApplyExecution command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ExecutionModel.ValidateCommandId(command.CommandId);
        ArgumentNullException.ThrowIfNull(command.Command);

        return command.Command switch
        {
            StartExecution start => await _starter.StartAsync(command.CommandId, start)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext),
            CancelExecution => await _canceller.CancelAsync(
                    command.CommandId,
                    command.ExpectedRevision)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext),
            ResolveOperation resolve => await _operationResolver.ResolveAsync(
                    command.CommandId,
                    command.ExpectedRevision,
                    resolve)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext),
            _ => throw new NeuronAuthorizationException(
                $"Execution '{_runtime.Id}' does not understand apply command "
                + $"'{command.Command.GetType().Name}'."),
        };
    }
}
