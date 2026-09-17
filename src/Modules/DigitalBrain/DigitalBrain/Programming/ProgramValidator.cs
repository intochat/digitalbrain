using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Abstractions.Programming;

namespace DigitalBrain.Core.Programming;

public static partial class ProgramValidator
{
    public const int MaxNodes = 64;
    public const int MaxSynapses = 256;
    public const int MaxDefinitionBytes = 65_536;

    private static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "input", "template", "filter", "map", "aggregate", "output", "call", "agent", "code",
    };

    private static readonly HashSet<string> FilterOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "eq", "equals", "==", "ne", "neq", "notequals", "!=", "gt", ">", "gte", ">=",
        "lt", "<", "lte", "<=", "contains", "startsWith", "endsWith", "exists", "in",
    };

    private static readonly HashSet<string> AggregateOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "count", "sum", "avg", "average", "min", "max",
    };

    private static readonly HashSet<string> ReservedSignals = new(StringComparer.Ordinal)
    {
        "ProgramChange", "ProgramRegistered", "ProgramStart", "ProgramStep", "ProgramCompleted",
        "ProgramCancel", "ProgramFinished", "ProgramRejected",
    };

    public static ProgramValidation Validate(ProgramDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        List<string> errors = [];
        if (string.IsNullOrWhiteSpace(definition.Id) || !ProgramId().IsMatch(definition.Id))
        {
            errors.Add("Program id must be 1–48 letters, digits, underscores, or hyphens, starting with a letter or digit.");
        }
        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > 200)
        {
            errors.Add("Program name must contain 1–200 characters.");
        }
        if (string.IsNullOrWhiteSpace(definition.Trigger) || !SignalType().IsMatch(definition.Trigger))
        {
            errors.Add("Program trigger must be a signal type of 1–64 letters, such as LeadReceived.");
        }
        else if (ReservedSignals.Contains(definition.Trigger))
        {
            errors.Add($"Trigger '{definition.Trigger}' is reserved by the programming runtime.");
        }
        if (definition.Description?.Length > 4_000)
        {
            errors.Add("Program description must contain at most 4,000 characters.");
        }
        if (definition.Nodes is null || definition.Nodes.Count is < 1 or > MaxNodes)
        {
            errors.Add($"A program must contain 1–{MaxNodes} nodes.");
        }
        if (definition.Synapses is null || definition.Synapses.Count > MaxSynapses)
        {
            errors.Add($"A program must provide at most {MaxSynapses} synapses.");
        }
        if (definition.Nodes is null || definition.Synapses is null
            || definition.Nodes.Count > MaxNodes || definition.Synapses.Count > MaxSynapses)
        {
            return new(false, errors, []);
        }

        var nodes = new Dictionary<string, ProgramNode>(StringComparer.Ordinal);
        var configBytes = 0;
        foreach (var node in definition.Nodes)
        {
            if (node is null || string.IsNullOrWhiteSpace(node.Id))
            {
                errors.Add("Every node needs an id.");
                continue;
            }
            if (!NodeId().IsMatch(node.Id))
            {
                errors.Add($"Node id '{node.Id}' must start with a letter and contain at most 64 letters, digits, underscores, or hyphens.");
            }
            if (!nodes.TryAdd(node.Id, node))
            {
                errors.Add($"Node id '{node.Id}' is repeated.");
            }
            if (string.IsNullOrWhiteSpace(node.Kind) || !Kinds.Contains(node.Kind))
            {
                errors.Add($"Node '{node.Id}' has unknown kind '{node.Kind}'.");
            }
            if (string.IsNullOrWhiteSpace(node.InputType) || node.InputType.Length > 128
                || string.IsNullOrWhiteSpace(node.OutputType) || node.OutputType.Length > 128)
            {
                errors.Add($"Node '{node.Id}' requires input and output type names of 1–128 characters.");
            }
            if (node.Config.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"Node '{node.Id}' configuration must be a JSON object.");
                continue;
            }

            configBytes += Encoding.UTF8.GetByteCount(node.Config.GetRawText());
            ValidateConfig(node, errors);
        }
        if (configBytes > MaxDefinitionBytes)
        {
            errors.Add("Program node configuration exceeds 64 KB. Store large data externally and pass a reference.");
        }
        if (!nodes.Values.Any(node => string.Equals(node.Kind, "input", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("A program requires at least one input node.");
        }

        var indegree = nodes.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var outgoing = nodes.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        var edges = new HashSet<(string From, string To)>();
        foreach (var edge in definition.Synapses)
        {
            if (edge is null || string.IsNullOrWhiteSpace(edge.From) || string.IsNullOrWhiteSpace(edge.To))
            {
                errors.Add("Every synapse requires from and to node ids.");
                continue;
            }
            if (!nodes.TryGetValue(edge.From, out var from) || !nodes.TryGetValue(edge.To, out var to))
            {
                errors.Add($"Synapse '{edge.From}' → '{edge.To}' references a missing node.");
                continue;
            }
            if (!edges.Add((edge.From, edge.To)))
            {
                errors.Add($"Synapse '{edge.From}' → '{edge.To}' is repeated.");
                continue;
            }

            indegree[edge.To]++;
            outgoing[edge.From].Add(edge.To);
            if (!string.Equals(from.OutputType, "Json", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(to.InputType, "Json", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(from.OutputType, to.InputType, StringComparison.Ordinal))
            {
                errors.Add($"Synapse '{edge.From}' → '{edge.To}' cannot connect output type '{from.OutputType}' to input type '{to.InputType}'.");
            }
        }

        foreach (var node in nodes.Values)
        {
            var isInput = string.Equals(node.Kind, "input", StringComparison.OrdinalIgnoreCase);
            if (indegree[node.Id] == 0 && !isInput)
            {
                errors.Add($"Node '{node.Id}' is disconnected: every root must be an input node.");
            }
            if (indegree[node.Id] > 0 && isInput)
            {
                errors.Add($"Input node '{node.Id}' cannot have incoming synapses.");
            }
        }

        var ready = new Queue<string>(nodes.Keys.Where(id => indegree[id] == 0));
        List<string> order = [];
        while (ready.TryDequeue(out var id))
        {
            order.Add(id);
            foreach (var target in outgoing[id])
            {
                if (--indegree[target] == 0)
                {
                    ready.Enqueue(target);
                }
            }
        }
        if (order.Count != nodes.Count)
        {
            errors.Add("Program synapses contain a cycle. Use a new trigger for a later reaction.");
        }

        return new(errors.Count == 0, errors, order);
    }

    private static void ValidateConfig(ProgramNode node, List<string> errors)
    {
        var config = node.Config;
        ValidateString(config, "path", node.Id, errors);
        switch (node.Kind?.ToLowerInvariant())
        {
            case "template":
                RequireTemplate(node, errors, "template", "text");
                break;
            case "map":
                RequireTemplate(node, errors, "template", "fields");
                break;
            case "output":
                if (ProgramBuiltins.TryProperty(config, out var template, "template", "value"))
                {
                    ValidateTemplate(node.Id, template, errors);
                }
                break;
            case "filter":
                ValidatePredicate(node.Id, config, errors, 0);
                break;
            case "aggregate":
                ValidateString(config, "field", node.Id, errors);
                if (ProgramBuiltins.TryProperty(config, out var operation, "operation")
                    && (operation.ValueKind != JsonValueKind.String || !AggregateOperations.Contains(operation.GetString()!)))
                {
                    errors.Add($"Node '{node.Id}' aggregate operation must be count, sum, avg, min, or max.");
                }
                break;
            case "call":
                RequireString(config, "neuron", node.Id, errors);
                RequireString(config, "interface", node.Id, errors);
                RequireString(config, "method", node.Id, errors);
                if (ProgramBuiltins.TryProperty(config, out var arguments, "arguments"))
                {
                    ValidateTemplate(node.Id, arguments, errors);
                }
                break;
            case "agent":
                ValidateString(config, "instructions", node.Id, errors);
                ValidateString(config, "mode", node.Id, errors);
                ValidateString(config, "prompt", node.Id, errors);
                break;
            case "code":
                RequireString(config, "source", node.Id, errors);
                break;
        }
    }

    private static void RequireTemplate(ProgramNode node, List<string> errors, params string[] names)
    {
        if (!ProgramBuiltins.TryProperty(node.Config, out var template, names))
        {
            errors.Add($"Node '{node.Id}' requires '{names[0]}' or '{names[1]}' in its configuration.");
            return;
        }
        ValidateTemplate(node.Id, template, errors);
    }

    private static void ValidateTemplate(string id, JsonElement template, List<string> errors)
    {
        try
        {
            _ = ProgramExpressions.Resolve(template, ProgramExpressions.Null, ProgramExpressions.Null,
                new Dictionary<string, JsonElement>(StringComparer.Ordinal));
        }
        catch (ArgumentException error)
        {
            errors.Add($"Node '{id}' has an invalid template: {error.Message}");
        }
    }

    private static void ValidatePredicate(string id, JsonElement predicate, List<string> errors, int depth)
    {
        if (depth > 16 || predicate.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"Node '{id}' predicates must be objects nested at most 16 levels.");
            return;
        }
        foreach (var group in new[] { "all", "any" })
        {
            if (ProgramBuiltins.TryProperty(predicate, out var children, group))
            {
                if (children.ValueKind != JsonValueKind.Array || children.GetArrayLength() is < 1 or > 64)
                {
                    errors.Add($"Node '{id}' '{group}' predicate must contain 1–64 predicates.");
                }
                else
                {
                    foreach (var child in children.EnumerateArray())
                    {
                        ValidatePredicate(id, child, errors, depth + 1);
                    }
                }
                return;
            }
        }
        if (ProgramBuiltins.TryProperty(predicate, out var not, "not"))
        {
            ValidatePredicate(id, not, errors, depth + 1);
            return;
        }

        ValidateString(predicate, "path", id, errors);
        var operation = "eq";
        if (ProgramBuiltins.TryProperty(predicate, out var configuredOperator, "operator"))
        {
            if (configuredOperator.ValueKind != JsonValueKind.String
                || !FilterOperators.Contains(configuredOperator.GetString()!))
            {
                errors.Add($"Node '{id}' uses an unsupported filter operator.");
            }
            else
            {
                operation = configuredOperator.GetString()!;
            }
        }
        if (ProgramBuiltins.TryProperty(predicate, out var expected, "value"))
        {
            ValidateTemplate(id, expected, errors);
        }
        else if (!string.Equals(operation, "exists", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Node '{id}' filter requires a comparison value.");
        }
    }

    private static void ValidateString(JsonElement config, string property, string id, List<string> errors)
    {
        if (ProgramBuiltins.TryProperty(config, out var value, property) && value.ValueKind != JsonValueKind.String)
        {
            errors.Add($"Node '{id}' configuration '{property}' must be a string.");
        }
    }

    private static void RequireString(JsonElement config, string property, string id, List<string> errors)
    {
        if (!ProgramBuiltins.TryProperty(config, out var value, property)
            || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            errors.Add($"Node '{id}' requires a nonempty '{property}' string in its configuration.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,47}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProgramId();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex NodeId();

    [GeneratedRegex("^[A-Za-z]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SignalType();
}
