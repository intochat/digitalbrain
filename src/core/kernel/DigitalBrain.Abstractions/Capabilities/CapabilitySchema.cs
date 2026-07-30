using System.Text.Json;
using System.Text.Json.Schema;

namespace DigitalBrain.Abstractions;

public static class CapabilitySchema
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string For(Type synapseType)
    {
        ArgumentNullException.ThrowIfNull(synapseType);

        var schema = Options.GetJsonSchemaAsNode(synapseType, JsonSchemaExporterOptions.Default);
        return schema.ToJsonString();
    }
}
