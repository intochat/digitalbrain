using System.Text.RegularExpressions;

namespace DigitalBrain.Apps;

public static partial class AppCompositionValidation
{
    public static void Validate(AppComposition graph)
    {
        Require(Enum.IsDefined(graph.Activation), "Unknown activation policy.");
        Require(graph.Parts is { Count: > 0 and <= 32 } && graph.Bindings is { Count: > 0 and <= 128 }, "An app needs 1-32 parts and 1-128 bindings.");
        Require(graph.Defaults is { Count: <= 32 } && graph.RequiredModules is { Count: <= 32 }, "Too many configuration fields or module requirements.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in graph.Parts!)
        {
            Require(part is not null && NamePattern().IsMatch(part.Name ?? "") && names.Add(part.Name!), "Part names must be unique local identifiers.");
            Require(!string.IsNullOrWhiteSpace(part!.Behavior) && part.Behavior.Length <= 100, "A behavior ID is required.");
            ValidateMap(part.Settings);
        }
        ValidateMap(graph.Defaults!);
        foreach (var module in graph.RequiredModules!) { Require(!string.IsNullOrWhiteSpace(module) && module.Length <= 200, "Invalid module requirement."); }
        var unique = new HashSet<AppBinding>();
        foreach (var binding in graph.Bindings!)
        {
            Require(binding is not null && unique.Add(binding), "Bindings must be unique.");
            Require(binding!.Source is "$app" or "$brain" || names.Contains(binding.Source), "Binding source is not a local part.");
            Require(binding.Source != "$brain" || binding.Signal == "activated", "The brain emits the activated signal.");
            Require(names.Contains(binding.Target), "Binding target is not a local part.");
            Require(NamePattern().IsMatch(binding.Signal ?? ""), "Invalid signal name.");
        }
        var visiting = new HashSet<string>();
        var visited = new HashSet<string>();
        void Visit(string name)
        {
            Require(!visiting.Contains(name), "App bindings must not contain cycles.");
            if (!visited.Add(name)) { return; }
            visiting.Add(name);
            foreach (var edge in graph.Bindings!.Where(edge => edge.Source == name)) { Visit(edge.Target); }
            visiting.Remove(name);
        }
        Visit("$app");
        Visit("$brain");
        Require(names.All(visited.Contains), "Every part must be reachable from an app signal.");
        foreach (var name in names) { Visit(name); }
        // Acyclic graphs can still expand exponentially. Count deliveries for each external
        // signal before accepting a package, using the same completed edges as execution.
        var costs = new Dictionary<string, int>();
        int Cost(string name)
        {
            if (costs.TryGetValue(name, out var cached)) { return cached; }
            var cost = 1;
            foreach (var edge in graph.Bindings!.Where(edge => edge.Source == name && edge.Signal == "completed"))
            { cost = Math.Min(257, cost + Cost(edge.Target)); }
            costs[name] = cost;
            return cost;
        }
        foreach (var group in graph.Bindings!.Where(edge => edge.Source is "$app" or "$brain").GroupBy(edge => (edge.Source, edge.Signal)))
        { Require(group.Sum(edge => Cost(edge.Target)) <= 256, "A signal may invoke at most 256 behavior parts."); }
    }

    public static void ValidateMap(IReadOnlyDictionary<string, string>? values)
    {
        Require(values is { Count: <= 32 }, "A configuration map supports at most 32 fields.");
        foreach (var pair in values!)
        {
            Require(NamePattern().IsMatch(pair.Key) && pair.Value is { Length: <= 4096 }, "Configuration keys must be local identifiers and values at most 4096 characters.");
            Require(!pair.Key.Contains("password", StringComparison.OrdinalIgnoreCase) && !pair.Key.Contains("secret", StringComparison.OrdinalIgnoreCase)
                && !pair.Key.Contains("token", StringComparison.OrdinalIgnoreCase) && !pair.Key.Contains("apikey", StringComparison.OrdinalIgnoreCase), "Credentials cannot be included in app packages.");
        }
    }

    private static void Require(bool valid, string message) { if (!valid) { throw new AppManifestException(message); } }
    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_.-]{0,63}$")]
    private static partial Regex NamePattern();
}
