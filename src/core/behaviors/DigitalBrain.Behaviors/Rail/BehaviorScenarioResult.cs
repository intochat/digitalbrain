namespace DigitalBrain.Behaviors;

public sealed record BehaviorScenarioResult(
    string ScenarioId,
    string Title,
    string BindingKey,
    bool Passed,
    string Detail);
