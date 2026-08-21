namespace DigitalBrain.AI.PersonaPlex;

public enum PersonaPlexReadinessState
{
    Disabled,
    Loading,
    Ready,
    Failed,
}

public sealed record PersonaPlexReadiness(
    PersonaPlexReadinessState State,
    string Message,
    bool IsModelConfigurationValid);
