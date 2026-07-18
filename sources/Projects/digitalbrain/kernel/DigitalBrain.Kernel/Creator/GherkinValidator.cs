using Gherkin;

namespace DigitalBrain.Kernel.Creator;

public sealed class GherkinValidator
{
    public bool Validate(string featureText, out IReadOnlyList<string> errors)
    {
        try
        {
            new Parser().Parse(new StringReader(featureText));
            errors = Array.Empty<string>();
            return true;
        }
        catch (CompositeParserException ex)
        {
            errors = ex.Errors.Select(e => e.Message).ToArray();
            return false;
        }
    }
}
