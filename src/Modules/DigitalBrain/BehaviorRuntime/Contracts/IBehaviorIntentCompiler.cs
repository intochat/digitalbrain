using DigitalBrain.Abstractions.Behavior;

namespace DigitalBrain.Core.Behavior;

public interface IBehaviorIntentCompiler
{
    Task<BehaviorCompilation> CompileAsync(string intent, CancellationToken cancellationToken = default);
}

public sealed record BehaviorCompilation(BehaviorDefinition Definition, BehaviorValidation Validation);
