using System.Text.Json;
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
        return schema.ToJsonString();
    }
}
