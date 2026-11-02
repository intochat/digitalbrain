using DigitalBrain.Rules;

namespace DigitalBrain.Examples.Calculator;

/// <summary>The display shows what a scenario expects.</summary>
internal sealed record DisplayShows(string Expected, string Actual) : Rule("display.shows")
{
    public override bool Broken => Actual != Expected;

    public override string Message => $"Expected display '{Expected}', got '{Actual}'.";
}
