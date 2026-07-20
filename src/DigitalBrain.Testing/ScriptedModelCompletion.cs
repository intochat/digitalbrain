using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

internal sealed class ScriptedModelCompletion : IModelCompletionService
{
    public Task<string> CompleteAsync(ModelTier tier, string prompt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        return Task.FromResult(SimulationCluster.Model(tier).Complete(prompt));
    }
}
