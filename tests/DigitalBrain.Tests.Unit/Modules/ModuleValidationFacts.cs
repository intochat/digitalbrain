using DigitalBrain.Modules;
using DigitalBrain.Modules.Validation;

namespace DigitalBrain.Tests.Unit.Modules;

public sealed class ModuleValidationFacts
{
    private sealed class Positive : IModuleValidator<int>
    {
        public ValidationIssue[] Validate(int value)
            => value > 0 ? [] : [new("Value", "positive", "Must be positive.")];
    }

    private sealed class NullReturning : IModuleValidator<string>
    {
        public ValidationIssue[] Validate(string value) => null!;
    }

    [Fact]
    public void Validate_returns_the_validators_issues()
    {
        var issues = ModuleValidation.Validate(-1, new Positive());
        Assert.Equal([new ValidationIssue("Value", "positive", "Must be positive.")], issues);
    }

    [Fact]
    public void ThrowIfInvalid_passes_a_valid_value()
        => ModuleValidation.ThrowIfInvalid(1, new Positive());

    [Fact]
    public void ThrowIfInvalid_throws_with_a_copy_of_the_issues()
    {
        var failure = Assert.Throws<ModuleValidationException>(() => ModuleValidation.ThrowIfInvalid(0, new Positive()));
        Assert.Equal("Module validation failed.", failure.Message);
        Assert.Single(failure.Issues);
        Assert.Equal("positive", failure.Issues[0].Code);
    }

    [Fact]
    public void A_validator_returning_null_is_a_contract_violation()
        => Assert.Throws<InvalidOperationException>(() => ModuleValidation.Validate("x", new NullReturning()));

    [Fact]
    public void Null_value_or_validator_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => ModuleValidation.Validate<string>(null!, new NullReturning()));
        Assert.Throws<ArgumentNullException>(() => ModuleValidation.Validate("x", null!));
    }
}
