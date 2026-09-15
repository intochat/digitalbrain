namespace DigitalBrain.Core.Behaviors;

/// <summary>Runtime reasoning without tools. Implementations return one JSON decision.</summary>
public interface IBehaviorDecision
{
    Task<string> DecideAsync(string instructions, string input, string outputSchema,
        string? provider, string? model, CancellationToken cancellationToken);
}
