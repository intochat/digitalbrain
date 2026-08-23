using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Execution;

[GrainType("preferencestore")]
internal sealed class PreferenceStoreEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<OwnerPreferences> state)
    : Entity<OwnerPreferences>(state), IPreferenceStore
{
    public async Task AddRule(string category, string ruleText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleText);

        var rules = new List<PreferenceRule>(State?.Rules.Count ?? 0);
        if (State?.Rules is { } existing)
        {
            rules.AddRange(existing);
        }

        rules.Add(new PreferenceRule(category.Trim(), ruleText.Trim()));
        await SaveAsync(new OwnerPreferences(rules));
    }

    public Task<IReadOnlyList<PreferenceRule>> ListRules()
        => Task.FromResult<IReadOnlyList<PreferenceRule>>(State?.Rules ?? []);
}
