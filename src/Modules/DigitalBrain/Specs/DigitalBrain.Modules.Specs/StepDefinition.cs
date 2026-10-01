using System.Text;
using System.Text.RegularExpressions;

namespace DigitalBrain.Specs;

internal sealed record StepDefinition(string Pattern, string Description, string Library, Regex Matcher, IReadOnlyList<string> Kinds,
    Func<StepContext, StepArguments, Task> Run)
{
    public static StepDefinition Compile(string pattern, string description, string library, Func<StepContext, StepArguments, Task> run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var regex = new StringBuilder("^");
        var kinds = new List<string>();
        foreach (var part in Regex.Split(pattern, @"(\{string\}|\{int\})"))
        {
            switch (part)
            {
                case "{string}": regex.Append("\"([^\"]*)\""); kinds.Add("string"); break;
                case "{int}": regex.Append(@"(-?\d+)"); kinds.Add("int"); break;
                default: regex.Append(Regex.Escape(part)); break;
            }
        }
        regex.Append('$');
        return new(pattern, description, library, new Regex(regex.ToString(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase), kinds, run);
    }
}
