using DigitalBrain.Modules;
using DigitalBrain.Rules;

namespace DigitalBrain.Modules.Validation;

public sealed class ModuleValidationException : InvalidOperationException
{
    public ModuleValidationException(ValidationIssue[] issues)
        : base("Module validation failed.")
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = [.. issues];
    }

    public ModuleValidationException(Rejected rejected)
        : this(IssuesOf(rejected))
    {
    }

    public ValidationIssue[] Issues { get; }

    // A rule name is "code:path"; a name without a colon has no path.
    private static ValidationIssue[] IssuesOf(Rejected rejected)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        var issues = new ValidationIssue[rejected.Rules.Length];
        for (var i = 0; i < issues.Length; i++)
        {
            var name = rejected.Rules[i];
            var colon = name.IndexOf(':', StringComparison.Ordinal);
            issues[i] = colon < 0
                ? new ValidationIssue(string.Empty, name, rejected.Messages[i])
                : new ValidationIssue(name[(colon + 1)..], name[..colon], rejected.Messages[i]);
        }

        return issues;
    }
}
