namespace DigitalBrain.Abstractions;

public interface IModelCompletionService
{
    Task<string> CompleteAsync(ModelTier tier, string prompt, CancellationToken cancellationToken);
}
