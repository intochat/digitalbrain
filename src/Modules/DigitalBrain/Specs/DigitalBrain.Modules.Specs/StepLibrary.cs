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
