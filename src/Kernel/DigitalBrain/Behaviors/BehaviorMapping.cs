using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core.Behaviors;
internal static class BehaviorMapping
{
    internal static NeuronId Target(JsonElement config) => NeuronId.TryParse(config.GetProperty("target").GetString(), out var id) ? id : throw new ArgumentException("An action target needs a valid typed neuron id.");
    internal static CommandId ActionId(NeuronId node, SignalId signal) => new(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{node}/{signal}")).AsSpan(0, 16)));
    internal static string OwnedName(NeuronId behavior, string run, string role) => "b-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{behavior}/{run}/{role}")))[..40];
    internal static string WithoutCommandIdentity(string schema, string? property)
    {
        if (property is null)
        {
            return schema;
        }

        var root = JsonNode.Parse(schema)!.AsObject();
        if (root["properties"] is JsonObject properties)
        {
            properties.Remove(property);
        }
        if (root["required"] is JsonArray required)
        {
            for (var i = required.Count - 1; i >= 0; i--)
            {
                if (required[i]?.GetValue<string>() == property)
                {
                    required.RemoveAt(i);
                }
            }
        }

        return root.ToJsonString();
    }

    internal static JsonElement Field(JsonElement input, string path)
    {
        var value = input;
        foreach (var part in path.Split('.'))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out value))
            {
                throw new ArgumentException($"Required field '{path}' is missing.");
            }
        }

        return value;
    }

    internal static string Map(JsonElement template, string input, SignalId signal)
    {
        using var document = JsonDocument.Parse(input);
        return Expand(template, document.RootElement, signal)?.ToJsonString() ?? "null";
    }

    private static JsonNode? Expand(JsonElement value, JsonElement input, SignalId signal)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var token = value.GetString()!;
            if (token.StartsWith("$input.", StringComparison.Ordinal))
            {
                return JsonNode.Parse(Field(input, token[7..]).GetRawText());
            }

            if (token == "$signalId")
            {
                return JsonValue.Create(signal.ToString());
            }
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            var output = new JsonObject();
            foreach (var property in value.EnumerateObject())
            {
                output[property.Name] = Expand(property.Value, input, signal);
            }

            return output;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return new JsonArray(value.EnumerateArray().Select(item => Expand(item, input, signal)).ToArray());
        }

        return JsonNode.Parse(value.GetRawText());
    }

    internal static string InferSchema(JsonElement template, string inputSchema)
    {
        using var input = JsonDocument.Parse(inputSchema);
        return Infer(template, input.RootElement).ToJsonString();
    }

    private static JsonNode FieldSchema(JsonElement input, string path)
    {
        var schema = input;
        foreach (var part in path.Split('.'))
        {
            if (!schema.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "object"
                || !schema.TryGetProperty("required", out var required) || !required.EnumerateArray().Any(item => item.GetString() == part) || !schema.TryGetProperty("properties", out var properties) || !properties.TryGetProperty(part, out schema))
            {
                throw new ArgumentException($"Mapping field '{path}' must be required in the input contract. Use an explicit object schema.");
            }
        }

        return JsonNode.Parse(schema.GetRawText())!;
    }

    private static JsonNode Infer(JsonElement value, JsonElement input)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            if (text.StartsWith("$input.", StringComparison.Ordinal))
            {
                return FieldSchema(input, text[7..]);
            }

            if (text == "$signalId")
            {
                return new JsonObject
                {
                    ["type"] = "string"
                };
            }
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = new JsonObject();
            var required = new JsonArray();
            foreach (var property in value.EnumerateObject())
            {
                properties[property.Name] = Infer(property.Value, input);
                required.Add(property.Name);
            }

            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required,
                ["additionalProperties"] = false
            };
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            var alternatives = new JsonArray(value.EnumerateArray().Select(item => Infer(item, input)).ToArray());
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = alternatives.Count == 0 ? JsonValue.Create(false) : new JsonObject
                {
                    ["anyOf"] = alternatives
                }
            };
        }

        var type = value.ValueKind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => value.TryGetInt64(out _) ? "integer" : "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            _ => "null",
        };
        return new JsonObject
        {
            ["type"] = type,
            ["const"] = JsonNode.Parse(value.GetRawText())
        };
    }

    internal static void ValidateFilter(JsonElement config, string inputSchema)
    {
        using var input = JsonDocument.Parse(inputSchema);
        var field = FieldSchema(input.RootElement, config.GetProperty("path").GetString()!);
        var op = config.GetProperty("operator").GetString();
        var value = config.GetProperty("value");
        if (op is not ("equals" or "contains" or "greaterThan" or "lessThan"))
        {
            throw new ArgumentException("Unknown filter operator.");
        }

        if (BehaviorSchema.Validate(field.ToJsonString(), value.GetRawText()).Count != 0)
        {
            throw new ArgumentException("Filter value does not match its input field.");
        }

        if (op == "contains" && field["type"]?.ToString() != "string")
        {
            throw new ArgumentException("contains requires a string field.");
        }

        if (op is "greaterThan" or "lessThan" && field["type"]?.ToString() is not ("number" or "integer"))
        {
            throw new ArgumentException("Numeric comparison requires a number field.");
        }

        if (op is "greaterThan" or "lessThan")
        {
            _ = Number(value);
        }
    }

    // Compare the exact decimal representation, including numbers outside IEEE-754/decimal
    // ranges. Bound token size before parsing arbitrary-length exponents; larger values fail closed.
    private static int CompareNumbers(JsonElement left, JsonElement right)
    {
        var first = Number(left);
        var second = Number(right);
        var signComparison = first.Sign.CompareTo(second.Sign);
        if (signComparison != 0)
        {
            return signComparison;
        }
        if (first.Sign == 0)
        {
            return 0;
        }
        var magnitudeComparison = first.Magnitude.CompareTo(second.Magnitude);
        if (magnitudeComparison != 0)
        {
            return first.Sign * magnitudeComparison;
        }
        var length = Math.Max(first.Digits.Length, second.Digits.Length);
        for (var index = 0; index < length; index++)
        {
            var firstDigit = index < first.Digits.Length ? first.Digits[index] : '0';
            var secondDigit = index < second.Digits.Length ? second.Digits[index] : '0';
            var comparison = firstDigit.CompareTo(secondDigit);
            if (comparison != 0)
            {
                return first.Sign * comparison;
            }
        }
        return 0;
    }

    private static (int Sign, BigInteger Magnitude, string Digits) Number(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number)
        {
            throw new ArgumentException("Numeric comparison requires JSON numbers.");
        }
        var text = value.GetRawText();
        if (text.Length > 4096)
        {
            throw new ArgumentException("Numeric filter operands may contain at most 4096 characters.");
        }
        var sign = text[0] == '-' ? -1 : 1;
        var start = sign < 0 ? 1 : 0;
        var exponentIndex = text.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? text[start..] : text[start..exponentIndex];
        var decimalIndex = mantissa.IndexOf('.');
        var fractionalDigits = decimalIndex < 0 ? 0 : mantissa.Length - decimalIndex - 1;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0)
        {
            return (0, BigInteger.Zero, "");
        }
        var exponent = exponentIndex < 0 ? BigInteger.Zero
            : BigInteger.Parse(text.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return (sign, exponent + digits.Length - fractionalDigits, digits.TrimEnd('0'));
    }
    internal static bool Matches(JsonElement config, string input)
    {
        using var document = JsonDocument.Parse(input);
        var actual = Field(document.RootElement, config.GetProperty("path").GetString()!);
        var expected = config.GetProperty("value");
        return config.GetProperty("operator").GetString() switch
        {
            "equals" => JsonElement.DeepEquals(actual, expected),
            "contains" => actual.GetString()!.Contains(expected.GetString()!, StringComparison.OrdinalIgnoreCase),
            "greaterThan" => CompareNumbers(actual, expected) > 0,
            "lessThan" => CompareNumbers(actual, expected) < 0,
            _ => throw new InvalidOperationException("Unknown filter operator."),
        };
    }
}


