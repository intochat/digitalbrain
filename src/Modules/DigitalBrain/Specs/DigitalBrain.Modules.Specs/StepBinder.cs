using System.Text.RegularExpressions;

namespace DigitalBrain.Specs;

internal sealed class StepBinder(IEnumerable<StepLibrary> libraries)
{
    private readonly IReadOnlyList<StepDefinition> _definitions = [.. libraries.SelectMany(library => library.Definitions)];

    public IReadOnlyList<StepPattern> Vocabulary => [.. _definitions.Select(definition => new StepPattern(definition.Pattern, definition.Library, definition.Description))];

    public BoundStep Bind(ParsedStep step)
    {
        foreach (var definition in _definitions)
        {
            var match = definition.Matcher.Match(step.Text);
            if (!match.Success) { continue; }
            // A quoted parameter is highlighted with its quotes.
            var parameters = match.Groups.Cast<Group>().Skip(1)
                .Select((group, index) => definition.Kinds[index] == "string"
                    ? new StepParameter(group.Index - 1, group.Length + 2, "string")
                    : new StepParameter(group.Index, group.Length, definition.Kinds[index]))
                .ToArray();
            return new(step.Line, step.Keyword, step.Text, definition.Pattern, parameters, step.DocString);
        }
        return new(step.Line, step.Keyword, step.Text, null, [], step.DocString);
    }

    public (StepDefinition Definition, StepArguments Arguments)? Resolve(BoundStep step)
    {
        var definition = _definitions.FirstOrDefault(item => item.Pattern == step.Pattern);
        if (definition is null) { return null; }
        var match = definition.Matcher.Match(step.Text);
        return match.Success ? (definition, new StepArguments([.. match.Groups.Cast<Group>().Skip(1).Select(group => group.Value)], step.DocString)) : null;
    }
}
