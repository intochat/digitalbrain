namespace DigitalBrain.Modules.Validation;

public sealed record ValidationIssue(string Path, string Code, string Message);
