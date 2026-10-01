namespace DigitalBrain.Specs;

internal sealed record ParsedStep(int Line, string Keyword, string Text, string? DocString);
