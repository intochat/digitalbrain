using System.Reflection;
using DigitalBrain.Platform.Contracts.Integrations;

namespace DigitalBrain.Platform.Integrations;

// A module declares what it needs with `public static IntegrationDefinition Integration` or
// `public static IntegrationDefinition[] Integrations`; a module with neither contributes nothing.
internal static class IntegrationDiscovery
{
    public static IReadOnlyList<IntegrationDefinition> Collect(IEnumerable<Type> moduleTypes)
    {
        var found = new List<IntegrationDefinition>();
        foreach (var module in moduleTypes.Distinct())
        {
            found.AddRange(Declared(module, "Integration"));
            found.AddRange(Declared(module, "Integrations"));
        }

        var duplicate = found.GroupBy(definition => definition.Id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Integration '{duplicate.Key}' is declared by more than one module.");
        }

        return found;
    }

    private static IEnumerable<IntegrationDefinition> Declared(Type module, string member)
    {
        const BindingFlags Static = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var value = module.GetProperty(member, Static)?.GetValue(null) ?? module.GetField(member, Static)?.GetValue(null);
        return value switch
        {
            IntegrationDefinition single => [single],
            IEnumerable<IntegrationDefinition> many => many,
            _ => [],
        };
    }
}
