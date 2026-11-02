namespace DigitalBrain.Modules.Validation;

public interface IModuleValidator<in T>
{
    ValidationIssue[] Validate(T value);
}
