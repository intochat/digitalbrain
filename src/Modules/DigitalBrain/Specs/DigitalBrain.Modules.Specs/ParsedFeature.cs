namespace DigitalBrain.Specs;

internal sealed record ParsedFeature(string Name, IReadOnlyList<ParsedStep> Background, IReadOnlyList<ParsedScenario> Scenarios);
