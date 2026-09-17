using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;


namespace DigitalBrain.Core.Behavior;

public interface IBehaviorRunLifecycle
{
    Task FinishedAsync(BehaviorDefinition definition, BehaviorRunSnapshot run, CancellationToken cancellationToken);
}
