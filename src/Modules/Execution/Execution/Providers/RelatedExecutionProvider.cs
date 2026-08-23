namespace DigitalBrain.Execution;

public sealed class RelatedExecutionProvider : IExecutionContextProvider
{
    public Task ContributeAsync(ExecutionSeedBuilder seed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        if (seed.RelatedExecutions.Count == 0)
        {
            return Task.CompletedTask;
        }

        var ids = new List<string>(seed.RelatedExecutions.Count);
        for (var i = 0; i < seed.RelatedExecutions.Count; i++)
        {
            ids.Add(seed.RelatedExecutions[i].ToString());
        }

        seed.PromptBlocks.Add("Related executions: " + string.Join(", ", ids));
        return Task.CompletedTask;
    }
}
