namespace DigitalBrain.Apps.Hosting;

public sealed record ProgramAssignment(
    string Owner,
    string Hash,
    IReadOnlyDictionary<string, PrebuiltBehavior> Behaviors,
    IReadOnlyDictionary<string, string> Settings);
