namespace DigitalBrain.Modules.Validation;

public static class ModuleValidation
{
    public static ValidationIssue[] Validate<T>(T value, IModuleValidator<T> validator)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(validator);
        return validator.Validate(value) ?? throw new InvalidOperationException("A module validator must return an issue array.");
    }

    public static void ThrowIfInvalid<T>(T value, IModuleValidator<T> validator)
    {
        var issues = Validate(value, validator);
        if (issues.Length != 0) { throw new ModuleValidationException(issues); }
    }
}
