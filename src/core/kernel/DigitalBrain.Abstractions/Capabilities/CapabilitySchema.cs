using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace DigitalBrain.Abstractions;

public static class CapabilitySchema
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static string For(Type synapseType)
    {
        ArgumentNullException.ThrowIfNull(synapseType);

        var schema = Options.GetJsonSchemaAsNode(synapseType, JsonSchemaExporterOptions.Default);
        NormalizeTypeKeywords(schema);
        return schema.ToJsonString();
    }

    public static string NormalizeForToolProviders(string jsonSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonSchema);

        var node = JsonNode.Parse(jsonSchema)
            ?? throw new InvalidOperationException("Capability JSON schema parsed to null.");
        NormalizeTypeKeywords(node);
        return node.ToJsonString();
    }

    private static void NormalizeTypeKeywords(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["type"] is JsonArray typeArray)
                {
                    string? chosen = null;
                    foreach (var entry in typeArray)
                    {
                        if (entry is JsonValue value
                            && value.TryGetValue<string>(out var typeName)
                            && !string.Equals(typeName, "null", StringComparison.Ordinal))
                        {
                            chosen = typeName;
                            break;
                        }
                    }

                    if (chosen is not null)
                    {
                        obj["type"] = chosen;
                    }
                }

                foreach (var property in obj.ToArray())
                {
                    NormalizeTypeKeywords(property.Value);
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    NormalizeTypeKeywords(item);
                }

                break;
        }
    }
}
