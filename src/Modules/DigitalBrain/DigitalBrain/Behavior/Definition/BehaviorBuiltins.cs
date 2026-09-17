using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;


namespace DigitalBrain.Core.Behavior;

public static class BehaviorBuiltins
{
    public static BehaviorNodeResult Execute(BehaviorNode node, JsonElement input, JsonElement value,
        IReadOnlyDictionary<string, JsonElement> outputs)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(outputs);
        return node.Kind.ToLowerInvariant() switch
        {
            "input" => new(BehaviorExpressions.Clone(input)),
            "template" => new(BehaviorExpressions.Resolve(Required(node.Config, "template", "text"), input, value, outputs)),
            "filter" => new(BehaviorExpressions.Clone(value), !Matches(node.Config, input, value, outputs)),
            "map" => new(Map(node.Config, input, value, outputs)),
            "aggregate" => new(Aggregate(node.Config, input, value, outputs)),
            "output" => new(TryProperty(node.Config, out var template, "template", "value")
                ? BehaviorExpressions.Resolve(template, input, value, outputs)
                : BehaviorExpressions.Clone(value)),
            _ => throw new ArgumentException($"Node kind '{node.Kind}' requires an execution extension.", nameof(node)),
        };
    }

    internal static bool TryProperty(JsonElement config, out JsonElement value, params string[] names)
    {
        if (config.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (config.TryGetProperty(name, out value))
                {
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static JsonElement Required(JsonElement config, params string[] names)
        => TryProperty(config, out var value, names) ? value
            : throw new ArgumentException($"Node configuration requires '{names[0]}'.", nameof(config));

    private static string String(JsonElement config, string name, string fallback = "")
        => TryProperty(config, out var value, name) && value.ValueKind == JsonValueKind.String ? value.GetString()! : fallback;

    private static JsonElement Select(JsonElement config, JsonElement input, JsonElement value,
        IReadOnlyDictionary<string, JsonElement> outputs)
        => BehaviorExpressions.ResolvePath(String(config, "path"), input, value, outputs);

    private static JsonElement Map(JsonElement config, JsonElement input, JsonElement value,
        IReadOnlyDictionary<string, JsonElement> outputs)
    {
        var source = Select(config, input, value, outputs);
        var template = Required(config, "template", "fields");
        if (source.ValueKind != JsonValueKind.Array)
        {
            return BehaviorExpressions.Resolve(template, input, source, outputs);
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var item in source.EnumerateArray())
            {
                BehaviorExpressions.Resolve(template, input, item, outputs).WriteTo(writer);
                BehaviorExpressions.RequireOutputRoom(writer);
            }
            writer.WriteEndArray();
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    private static JsonElement Aggregate(JsonElement config, JsonElement input, JsonElement value,
        IReadOnlyDictionary<string, JsonElement> outputs)
    {
        var source = Select(config, input, value, outputs);
        if (source.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("Aggregate path must resolve to an array.", nameof(config));
        }

        var operation = String(config, "operation", "count").ToLowerInvariant();
        var field = String(config, "field");
        var count = source.GetArrayLength();
        decimal total = 0;
        decimal? minimum = null;
        decimal? maximum = null;
        if (operation != "count")
        {
            foreach (var item in source.EnumerateArray())
            {
                var number = BehaviorExpressions.ReadPath(item, field);
                if (number.ValueKind != JsonValueKind.Number || !number.TryGetDecimal(out var amount))
                {
                    throw new ArgumentException($"Aggregate '{operation}' requires decimal numbers at '{(field.Length == 0 ? "value" : field)}'.", nameof(config));
                }
                total += amount;
                minimum = minimum is null ? amount : Math.Min(minimum.Value, amount);
                maximum = maximum is null ? amount : Math.Max(maximum.Value, amount);
            }
        }

        decimal? result = operation switch
        {
            "count" => count,
            "sum" => total,
            "avg" or "average" => count == 0 ? null : total / count,
            "min" => minimum,
            "max" => maximum,
            _ => throw new ArgumentException($"Unknown aggregate operation '{operation}'.", nameof(config)),
        };
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            if (result is { } number)
            {
                writer.WriteNumberValue(number);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    private static bool Matches(JsonElement predicate, JsonElement input, JsonElement value,
        IReadOnlyDictionary<string, JsonElement> outputs, int depth = 0)
    {
        if (depth > 16)
        {
            throw new ArgumentException("Filter predicates may nest at most 16 levels.", nameof(predicate));
        }
        if (TryProperty(predicate, out var all, "all"))
        {
            return all.EnumerateArray().All(item => Matches(item, input, value, outputs, depth + 1));
        }
        if (TryProperty(predicate, out var any, "any"))
        {
            return any.EnumerateArray().Any(item => Matches(item, input, value, outputs, depth + 1));
        }
        if (TryProperty(predicate, out var not, "not"))
        {
            return !Matches(not, input, value, outputs, depth + 1);
        }

        var actual = Select(predicate, input, value, outputs);
        var expected = TryProperty(predicate, out var requested, "value")
            ? BehaviorExpressions.Resolve(requested, input, value, outputs)
            : BehaviorExpressions.Null;
        var operation = String(predicate, "operator", "eq").ToLowerInvariant();
        return operation switch
        {
            "eq" or "equals" or "==" => JsonElement.DeepEquals(actual, expected),
            "ne" or "neq" or "notequals" or "!=" => !JsonElement.DeepEquals(actual, expected),
            "gt" or ">" => Compare(actual, expected) is > 0,
            "gte" or ">=" => Compare(actual, expected) is >= 0,
            "lt" or "<" => Compare(actual, expected) is < 0,
            "lte" or "<=" => Compare(actual, expected) is <= 0,
            "contains" => actual.ValueKind == JsonValueKind.Array
                ? actual.EnumerateArray().Any(item => JsonElement.DeepEquals(item, expected))
                : actual.ValueKind == JsonValueKind.String && expected.ValueKind == JsonValueKind.String
                    && actual.GetString()!.Contains(expected.GetString()!, StringComparison.OrdinalIgnoreCase),
            "startswith" => actual.ValueKind == JsonValueKind.String && expected.ValueKind == JsonValueKind.String
                && actual.GetString()!.StartsWith(expected.GetString()!, StringComparison.OrdinalIgnoreCase),
            "endswith" => actual.ValueKind == JsonValueKind.String && expected.ValueKind == JsonValueKind.String
                && actual.GetString()!.EndsWith(expected.GetString()!, StringComparison.OrdinalIgnoreCase),
            "exists" => actual.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined),
            "in" => expected.ValueKind == JsonValueKind.Array && expected.EnumerateArray().Any(item => JsonElement.DeepEquals(actual, item)),
            _ => throw new ArgumentException($"Unknown filter operator '{operation}'.", nameof(predicate)),
        };
    }

    private static int? Compare(JsonElement left, JsonElement right)
    {
        if (left.ValueKind == JsonValueKind.Number && right.ValueKind == JsonValueKind.Number)
        {
            if (left.TryGetDecimal(out var first) && right.TryGetDecimal(out var second))
            {
                return first.CompareTo(second);
            }
            return left.GetDouble().CompareTo(right.GetDouble());
        }
        return left.ValueKind == JsonValueKind.String && right.ValueKind == JsonValueKind.String
            ? string.Compare(left.GetString(), right.GetString(), StringComparison.Ordinal)
            : null;
    }
}
