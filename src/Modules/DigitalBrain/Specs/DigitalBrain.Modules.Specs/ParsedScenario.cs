namespace DigitalBrain.Specs;

internal sealed record ParsedScenario(string Name, int Line, IReadOnlyList<string> Tags, IReadOnlyList<ParsedStep> Steps);
