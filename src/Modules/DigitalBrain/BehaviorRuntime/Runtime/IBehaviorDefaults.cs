using DigitalBrain.Abstractions.Behavior;

namespace DigitalBrain.Core.Behavior;

public interface IBehaviorDefaults
{
    IReadOnlyList<BehaviorDefinition> Definitions { get; }
}
