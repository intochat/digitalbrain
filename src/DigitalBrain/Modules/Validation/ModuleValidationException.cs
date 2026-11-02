namespace DigitalBrain.Modules.Validation;

public sealed class ModuleValidationException : InvalidOperationException
{
    public ModuleValidationException(ValidationIssue[] issues)
        : base("Module validation failed.")
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = [.. issues];
    }

    public ValidationIssue[] Issues { get; }
}
