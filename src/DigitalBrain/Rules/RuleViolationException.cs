namespace DigitalBrain.Rules;

/// <summary>Thrown when an operation is refused; carries the published rejection.</summary>
public sealed class RuleViolationException(Rejected rejected) : Exception(string.Join(" ", rejected.Messages))
{
    /// <summary>The rejection.</summary>
    public Rejected Rejected { get; } = rejected;
}
