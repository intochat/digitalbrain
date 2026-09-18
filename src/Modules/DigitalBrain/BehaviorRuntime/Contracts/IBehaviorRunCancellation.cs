namespace DigitalBrain.Abstractions.Behavior;

public interface IBehaviorRunCancellation
{
    void Cancel(string behaviorId, string runId);
}
