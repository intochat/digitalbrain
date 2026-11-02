using DigitalBrain.Rules;

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

    public static void Check(string moduleId, params Rule[] rules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(rules);
        var rejected = DigitalBrain.Rules.Rules.Judge("module." + moduleId, rules);
        if (rejected is not null) { throw new ModuleValidationException(rejected); }
    }
}
