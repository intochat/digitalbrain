using System.Text;
using System.Text.RegularExpressions;

namespace DigitalBrain.Specs;

// A host-registered set of step phrasings. Only reviewed host code defines what a step does; an
// author or an agent can only compose the phrasings, never redefine them.
public abstract class StepLibrary
{
    private readonly List<StepDefinition> _definitions = [];

    public virtual string Name => GetType().Name;

    internal IReadOnlyList<StepDefinition> Definitions => _definitions;

    // {string} matches "quoted text" and {int} a whole number; everything else matches literally.
    protected void Step(string pattern, string description, Func<StepContext, StepArguments, Task> run)
        => _definitions.Add(StepDefinition.Compile(pattern, description, Name, run));
}

public sealed class StepContext(string subject, IGrainFactory grains, IServiceProvider services, CancellationToken cancellationToken)
{
    private readonly Dictionary<string, object> _items = new(StringComparer.Ordinal);

    public string Subject { get; } = subject;
    public IGrainFactory Grains { get; } = grains;
    public IServiceProvider Services { get; } = services;
    public CancellationToken CancellationToken { get; } = cancellationToken;

    // What one step leaves for the next, for example the last answer a When step received.
    public void Remember<T>(string name, T value) where T : notnull => _items[name] = value;

    public bool TryRecall<T>(string name, out T value)
    {
        var found = _items.TryGetValue(name, out var item) && item is T;
        value = found ? (T)item! : default!;
        return found;
    }

    public T Recall<T>(string name) => _items.TryGetValue(name, out var value) && value is T typed
        ? typed
        : throw new StepFailedException($"No earlier step produced '{name}'.");
}

public sealed class StepArguments(IReadOnlyList<string> values, string? docString)
{
    public string Text(int index) => values[index];
    public int Int(int index) => int.Parse(values[index], System.Globalization.CultureInfo.InvariantCulture);
    public string DocString => docString ?? throw new StepFailedException("This step needs a \"\"\" doc string below it.");
}

// A Then step that does not hold. Its message is what the author sees next to the red step.
public sealed class StepFailedException(string message) : Exception(message);

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
