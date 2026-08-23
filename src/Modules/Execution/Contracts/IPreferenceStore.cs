using DigitalBrain.Abstractions.Entities;

namespace DigitalBrain.Execution;

[Alias("preferences")]
public interface IPreferenceStore : IEntity<OwnerPreferences>
{
    const string DefaultInstanceName = "preferences";

    [Alias(nameof(AddRule))]
    Task AddRule(string category, string ruleText);

    [Alias(nameof(ListRules))]
    Task<IReadOnlyList<PreferenceRule>> ListRules();
}

[GenerateSerializer, Alias("db.preference-rule.v1")]
public sealed record PreferenceRule(
    [property: Id(0)] string Category,
    [property: Id(1)] string RuleText);

[GenerateSerializer, Alias("db.owner-preferences.v1")]
public sealed record OwnerPreferences(
    [property: Id(0)] IReadOnlyList<PreferenceRule> Rules);
