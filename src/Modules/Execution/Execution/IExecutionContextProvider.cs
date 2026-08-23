namespace DigitalBrain.Execution;

public interface IExecutionContextProvider
{
    Task ContributeAsync(ExecutionSeedBuilder seed, CancellationToken cancellationToken);
}
