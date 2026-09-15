using System.Text.Json;

namespace DigitalBrain.Core.Behaviors;
/// <summary>Validates a bounded, deliberately conservative subset of JSON Schema.</summary>
/// <remarks>
/// Supports boolean schemas, type, properties, required, boolean additionalProperties, items,
/// enum, const, anyOf, $defs and local JSON-pointer $ref. References may have annotation and
/// $defs siblings only. Other constraints are rejected. Inputs are limited to 1 MiB of UTF-16
/// characters, 64 JSON levels and 100,000 evaluation steps. Assignability proves structural
/// inclusion; false can also mean that inclusion could not be proved within these limits.
/// </remarks>
public static class BehaviorSchema
{
    private const int MaximumLength = 1_048_576;
    private const int MaximumDepth = 64;
    private const int MaximumSteps = 100_000;
    private static readonly string[] AllTypes = ["null", "boolean", "string", "integer", "number", "object", "array"];
    /// <summary>Returns errors for malformed or unsupported schemas; empty means supported.</summary>
    public static IReadOnlyList<string> CheckSchema(string schema)
    {
        try
        {
            using var document = Parse(schema);
            var budget = MaximumSteps;
            Check(document.RootElement, document.RootElement, new HashSet<string>(StringComparer.Ordinal), ref budget, 0);
            return [];
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return [exception.Message];
        }
    }

    /// <summary>Checks the schema and payload, returning an error when either is invalid.</summary>
    public static IReadOnlyList<string> Validate(string schema, string json)
    {
        var errors = CheckSchema(schema);
        if (errors.Count != 0)
        {
            return errors;
        }

        try
        {
            using var contract = Parse(schema);
            using var payload = Parse(json);
            var budget = MaximumSteps;
            return Matches(contract.RootElement, payload.RootElement, contract.RootElement, ref budget, 0) ? [] : ["Payload does not satisfy the behavior schema."];
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return [exception.Message];
        }
    }

    /// <summary>Proves that every output accepted by the source is accepted by the target.</summary>
    public static bool IsAssignable(string outputSchema, string inputSchema)
    {
        if (CheckSchema(outputSchema).Count != 0 || CheckSchema(inputSchema).Count != 0)
        {
            return false;
        }

        try
        {
            using var source = Parse(outputSchema);
            using var target = Parse(inputSchema);
            var budget = MaximumSteps;
            return Includes(source.RootElement, target.RootElement, source.RootElement, target.RootElement, ref budget, 0);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static JsonDocument Parse(string text)
    {
        if (text is null || text.Length > MaximumLength)
        {
            throw new ArgumentException("JSON is null or exceeds the 1 MiB character limit.");
        }

        var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = MaximumDepth });
        try
        {
            RejectDuplicates(document.RootElement);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new ArgumentException($"Duplicate JSON property '{property.Name}'.");
                }

                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                RejectDuplicates(item);
            }
        }
    }

    private static void Check(JsonElement schema, JsonElement root, HashSet<string> references, ref int budget, int depth)
    {
        if (--budget < 0 || depth > MaximumDepth)
        {
            throw new ArgumentException("Schema exceeds the traversal depth limit.");
        }

        if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return;
        }

        Require(schema.ValueKind == JsonValueKind.Object, "A schema must be an object or boolean.");
        var hasReference = schema.TryGetProperty("$ref", out _);
        foreach (var property in schema.EnumerateObject())
        {
            var value = property.Value;
            if (hasReference && property.Name is not ("$ref" or "$defs" or "$schema" or "title" or "description" or "default"))
            {
                throw new ArgumentException("$ref siblings may only be annotations or $defs.");
            }

            switch (property.Name)
            {
                case "$schema":
                case "title":
                case "description":
                    Require(value.ValueKind == JsonValueKind.String, $"{property.Name} must be a string.");
                    break;
                case "default":
                case "const":
                    break;
                case "type":
                    Require(value.ValueKind == JsonValueKind.String || value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0, "type must be a name or nonempty array.");
                    foreach (var type in value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [value])
                    {
                        Require(type.ValueKind == JsonValueKind.String && AllTypes.Contains(type.GetString(), StringComparer.Ordinal), "Unknown JSON schema type.");
                    }

                    break;
                case "properties":
                case "$defs":
                    Require(value.ValueKind == JsonValueKind.Object, $"{property.Name} must be an object.");
                    foreach (var entry in value.EnumerateObject())
                    {
                        Check(entry.Value, root, references, ref budget, depth + 1);
                    }

                    break;
                case "required":
                    Require(value.ValueKind == JsonValueKind.Array, "required must be an array.");
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var name in value.EnumerateArray())
                    {
                        Require(name.ValueKind == JsonValueKind.String && names.Add(name.GetString()!), "required must contain unique strings.");
                    }

                    break;
                case "additionalProperties":
                    Require(value.ValueKind is JsonValueKind.True or JsonValueKind.False, "Only boolean additionalProperties is supported.");
                    break;
                case "items":
                    Check(value, root, references, ref budget, depth + 1);
                    break;
                case "enum":
                    Require(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0, "enum must be a nonempty array.");
                    break;
                case "anyOf":
                    Require(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0, "anyOf must be a nonempty array.");
                    foreach (var option in value.EnumerateArray())
                    {
                        Check(option, root, references, ref budget, depth + 1);
                    }

                    break;
                case "$ref":
                    Require(value.ValueKind == JsonValueKind.String, "$ref must be a local JSON pointer.");
                    var reference = value.GetString()!;
                    var resolved = Resolve(root, reference);
                    if (references.Add(reference))
                    {
                        Check(resolved, root, references, ref budget, depth + 1);
                    }

                    break;
                default:
                    throw new ArgumentException($"Unsupported JSON schema keyword '{property.Name}'.");
            }
        }
    }

    private static JsonElement Resolve(JsonElement root, string reference)
    {
        Require(reference == "#" || reference.StartsWith("#/", StringComparison.Ordinal), "Only local JSON-pointer references are supported.");
        var current = root;
        if (reference == "#")
        {
            return current;
        }

        foreach (var encoded in reference[2..].Split('/'))
        {
            // URI escapes and malformed pointer escapes are deliberately outside this subset.
            Require(!encoded.Contains('%', StringComparison.Ordinal) && ValidPointerEscapes(encoded), "Unsupported reference escaping.");
            var key = encoded.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            current = current.ValueKind == JsonValueKind.Object && current.TryGetProperty(key, out var child)
                ? child
                : current.ValueKind == JsonValueKind.Array
                    && int.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
                    && index >= 0 && index < current.GetArrayLength()
                    ? current[index]
                    : throw new ArgumentException($"Unresolved schema reference '{reference}'.");
        }

        return current;
    }

    private static bool Matches(JsonElement schema, JsonElement value, JsonElement root, ref int budget, int depth)
    {
        Spend(ref budget, depth);
        if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return schema.ValueKind == JsonValueKind.True;
        }

        if (schema.TryGetProperty("$ref", out var reference))
        {
            return Matches(Resolve(root, reference.GetString()!), value, root, ref budget, depth + 1);
        }

        if (!Types(schema).Contains(Kind(value), StringComparer.Ordinal))
        {
            return false;
        }

        if (schema.TryGetProperty("const", out var constant) && !JsonElement.DeepEquals(constant, value))
        {
            return false;
        }

        if (schema.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(choice => JsonElement.DeepEquals(choice, value)))
        {
            return false;
        }

        if (schema.TryGetProperty("anyOf", out var alternatives))
        {
            var found = false;
            foreach (var alternative in alternatives.EnumerateArray())
            {
                if (Matches(alternative, value, root, ref budget, depth + 1))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            if (Required(schema).Any(name => !value.TryGetProperty(name, out _)))
            {
                return false;
            }

            foreach (var property in value.EnumerateObject())
            {
                if (Property(schema, property.Name, out var contract))
                {
                    if (!Matches(contract, property.Value, root, ref budget, depth + 1))
                    {
                        return false;
                    }
                }
                else if (!AllowsExtra(schema))
                {
                    return false;
                }
            }
        }

        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            foreach (var item in value.EnumerateArray())
            {
                if (!Matches(items, item, root, ref budget, depth + 1))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool Includes(JsonElement source, JsonElement target, JsonElement sourceRoot, JsonElement targetRoot, ref int budget, int depth)
    {
        Spend(ref budget, depth);
        if (source.ValueKind == JsonValueKind.False || target.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (target.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (source.ValueKind == JsonValueKind.Object && source.TryGetProperty("$ref", out var sourceRef))
        {
            return Includes(Resolve(sourceRoot, sourceRef.GetString()!), target, sourceRoot, targetRoot, ref budget, depth + 1);
        }

        if (target.TryGetProperty("$ref", out var targetRef))
        {
            return Includes(source, Resolve(targetRoot, targetRef.GetString()!), sourceRoot, targetRoot, ref budget, depth + 1);
        }

        // Only reference-free equality is a safe shortcut across different definition roots.
        if (!ContainsReference(source) && JsonElement.DeepEquals(source, target))
        {
            return true;
        }

        if (source.ValueKind == JsonValueKind.True)
        {
            return !target.EnumerateObject().Any(property => property.Name is not ("$schema" or "title" or "description" or "default" or "$defs"));
        }

        if (source.TryGetProperty("const", out var constant))
        {
            return Matches(target, constant, targetRoot, ref budget, depth + 1);
        }

        if (source.TryGetProperty("enum", out var choices))
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (!Matches(target, choice, targetRoot, ref budget, depth + 1))
                {
                    return false;
                }
            }

            return true;
        }

        if (source.TryGetProperty("anyOf", out var sourceOptions))
        {
            foreach (var option in sourceOptions.EnumerateArray())
            {
                if (!Includes(option, target, sourceRoot, targetRoot, ref budget, depth + 1))
                {
                    return false;
                }
            }

            return true;
        }

        if (!Types(source).IsSubsetOf(Types(target)))
        {
            return false;
        }

        if (target.TryGetProperty("const", out _) || target.TryGetProperty("enum", out _))
        {
            return false;
        }

        if (target.TryGetProperty("anyOf", out var targetOptions))
        {
            var found = false;
            foreach (var option in targetOptions.EnumerateArray())
            {
                if (Includes(source, option, sourceRoot, targetRoot, ref budget, depth + 1))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        if (Types(source).Contains("object"))
        {
            if (!Required(target).IsSubsetOf(Required(source)))
            {
                return false;
            }

            if (!AllowsExtra(target) && AllowsExtra(source))
            {
                return false;
            }

            if (target.TryGetProperty("properties", out var targetProperties))
            {
                foreach (var property in targetProperties.EnumerateObject())
                {
                    if (Property(source, property.Name, out var sourceProperty))
                    {
                        if (!Includes(sourceProperty, property.Value, sourceRoot, targetRoot, ref budget, depth + 1))
                        {
                            return false;
                        }
                    }
                    else if (AllowsExtra(source))
                    {
                        return false;
                    }
                }
            }

            if (!AllowsExtra(target) && source.TryGetProperty("properties", out var sourceProperties))
            {
                foreach (var property in sourceProperties.EnumerateObject())
                {
                    if (!Property(target, property.Name, out _))
                    {
                        return false;
                    }
                }
            }
        }

        if (Types(source).Contains("array") && target.TryGetProperty("items", out var targetItems))
        {
            if (!source.TryGetProperty("items", out var sourceItems))
            {
                return targetItems.ValueKind == JsonValueKind.True;
            }

            if (!Includes(sourceItems, targetItems, sourceRoot, targetRoot, ref budget, depth + 1))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsReference(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (property.NameEquals("$ref") || ContainsReference(property.Value))
                {
                    return true;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (ContainsReference(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static HashSet<string> Types(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            return new(AllTypes, StringComparer.Ordinal);
        }

        var types = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal) : new HashSet<string>([type.GetString()!], StringComparer.Ordinal);
        if (types.Contains("number"))
        {
            types.Add("integer");
        }

        return types;
    }

    private static string Kind(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => "null",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.String => "string",
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        JsonValueKind.Number => IsInteger(value.GetRawText()) ? "integer" : "number",
        _ => throw new ArgumentException("Unsupported JSON value.")};
    private static bool ValidPointerEscapes(string encoded)
    {
        for (var index = 0; index < encoded.Length; index++)
        {
            if (encoded[index] == '~' && (++index == encoded.Length || encoded[index] is not ('0' or '1')))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsInteger(string text)
    {
        var exponentIndex = text.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? text.AsSpan() : text.AsSpan(0, exponentIndex);
        var decimalIndex = mantissa.IndexOf('.');
        var fractionalDigits = decimalIndex < 0 ? 0 : mantissa.Length - decimalIndex - 1;
        var trailingZeros = 0;
        var nonzero = false;
        for (var index = mantissa.Length - 1; index >= 0; index--)
        {
            var digit = mantissa[index];
            if (digit is '-' or '.')
            {
                continue;
            }

            if (digit != '0')
            {
                nonzero = true;
            }
            else if (!nonzero)
            {
                trailingZeros++;
            }
        }

        if (!nonzero)
        {
            return true;
        }

        if (exponentIndex < 0)
        {
            return trailingZeros >= fractionalDigits;
        }

        var exponentText = text.AsSpan(exponentIndex + 1);
        if (!long.TryParse(exponentText, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var exponent))
        {
            return exponentText[0] != '-';
        }

        return exponent >= (long)fractionalDigits - trailingZeros;
    }

    private static HashSet<string> Required(JsonElement schema) => schema.TryGetProperty("required", out var required) ? required.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal) : new(StringComparer.Ordinal);
    private static bool AllowsExtra(JsonElement schema) => !schema.TryGetProperty("additionalProperties", out var extra) || extra.ValueKind == JsonValueKind.True;
    private static bool Property(JsonElement schema, string name, out JsonElement property)
    {
        property = default;
        return schema.TryGetProperty("properties", out var properties) && properties.TryGetProperty(name, out property);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new ArgumentException(message);
        }
    }

    private static void Spend(ref int budget, int depth)
    {
        if (--budget < 0 || depth > MaximumDepth)
        {
            throw new ArgumentException("Schema evaluation resource limit exceeded.");
        }
    }
}



