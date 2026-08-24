using System.Reflection;
using System.Text.RegularExpressions;
using Reqnroll;
using Reqnroll.Bindings;

namespace DigitalBrain.SmartPrompt;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class BehaviorStepAttribute(
    BehaviorStepRole role,
    string description,
    string? template = null) : Attribute
{
    public BehaviorStepRole Role { get; } = role;

    public string Description { get; } = description;

    public string? Template { get; } = template;
}

internal sealed record BehaviorStepDefinition(
    string Name,
    StepDefinitionType Type,
    BehaviorStepRole Role,
    Regex Expression,
    BehaviorStepSuggestion Suggestion);

internal sealed class BehaviorStepCatalog
{
    private BehaviorStepCatalog(IReadOnlyList<BehaviorStepDefinition> definitions)
    {
        Definitions = definitions;
        Suggestions = definitions
            .Select(static definition => definition.Suggestion)
            .Distinct()
            .ToArray();
    }

    public IReadOnlyList<BehaviorStepDefinition> Definitions { get; }

    public IReadOnlyList<BehaviorStepSuggestion> Suggestions { get; }

    public static BehaviorStepCatalog CreateDefault()
    {
        var definitions = typeof(BuiltInBehaviorSteps)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(CreateDefinitions)
            .OrderBy(static definition => definition.Name, StringComparer.Ordinal)
            .ToArray();
        return new BehaviorStepCatalog(definitions);
    }

    private static IEnumerable<BehaviorStepDefinition> CreateDefinitions(MethodInfo method)
    {
        var metadata = method.GetCustomAttribute<BehaviorStepAttribute>();
        if (metadata is null)
        {
            yield break;
        }

        foreach (var binding in method.GetCustomAttributes<StepDefinitionBaseAttribute>())
        {
            var expression = binding.Expression;
            var pattern = expression.StartsWith('^') ? expression : $"^(?:{expression})$";
            var type = binding.Types.Single();
            var keyword = type switch
            {
                StepDefinitionType.Given => "Given",
                StepDefinitionType.When => "When",
                StepDefinitionType.Then => "Then",
                _ => "Step",
            };
            yield return new BehaviorStepDefinition(
                method.Name,
                type,
                metadata.Role,
                new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)),
                new BehaviorStepSuggestion(keyword, metadata.Template ?? expression, metadata.Description));
        }
    }
}
