namespace DigitalBrain.Specs;

internal sealed class SpecSyntaxException(int line, string message) : Exception(message)
{
    public int Line { get; } = line;
}
