using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;


namespace DigitalBrain.Core.Behavior;

public interface IBehaviorNodeExecutor
{
    Task<JsonElement> ExecuteAsync(BehaviorExecutionContext context, CancellationToken cancellationToken);
}
