using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;


namespace DigitalBrain.Core.Behavior;

public interface IBehaviorRunCancellation
{
    void Cancel(string behaviorId, string runId);
}
