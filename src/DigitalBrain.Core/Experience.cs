namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("DigitalBrain.Core.Experience")]
public record Experience(
    string ExperienceId,
    string Name,
    string Kind,
    string Summary,
    IReadOnlyDictionary<string, object?> EntryAction);

[GenerateSerializer]
[Alias("DigitalBrain.Core.ExperienceStep")]
public record ExperienceStep(
    string Pack,
    string ExperienceId,
    string EventName,
    IReadOnlyDictionary<string, string> Args) : Synapse(nameof(ExperienceStep), DateTimeOffset.UtcNow);
