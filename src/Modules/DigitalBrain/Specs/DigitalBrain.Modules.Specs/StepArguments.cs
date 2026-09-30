using System.Text.RegularExpressions;

namespace DigitalBrain.Specs;

public sealed class StepArguments(IReadOnlyList<string> values, string? docString)
{
    public string Text(int index) => values[index];
    public int Int(int index) => int.Parse(values[index], System.Globalization.CultureInfo.InvariantCulture);
    public string DocString => docString ?? throw new StepFailedException("This step needs a \"\"\" doc string below it.");
}
