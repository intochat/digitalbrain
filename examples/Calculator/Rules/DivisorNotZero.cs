using DigitalBrain.Rules;

namespace DigitalBrain.Examples.Calculator;

/// <summary>A pending division cannot be evaluated by a zero divisor.</summary>
internal sealed record DivisorNotZero(string? PendingOperator, double Divisor) : Rule("divisor.not-zero")
{
    public override bool Broken => PendingOperator == "/" && Divisor == 0d;

    public override string Message => "Cannot divide by zero.";
}
