using DigitalBrain.Rules;

namespace DigitalBrain.Examples.Calculator;

/// <summary>A scenario expects the last key press to have been refused by a rule.</summary>
internal sealed record PressRejected(bool Rejected) : Rule("press.rejected")
{
    public override bool Broken => !Rejected;

    public override string Message => "Expected the key press to be rejected.";
}
