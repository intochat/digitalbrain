using System.Text.Json;
using DigitalBrain.Core;

namespace DigitalBrain.Mcp;

// Shared, transport-agnostic helpers for the DigitalBrain MCP tool surfaces. Reached through an in-process
// IGrainFactory when co-hosted in the kernel (HTTP) and the Orleans-client IGrainFactory in the stdio server.
// No fabricated fallbacks: real responses or honest errors only.
public abstract class DigitalBrainToolsBase(IGrainFactory grains)
{
    protected IGrainFactory Grains { get; } = grains;

    protected static readonly JsonSerializerOptions SurfaceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    protected static IEnumerable<string> SplitIds(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => !string.IsNullOrWhiteSpace(id));

    protected static string Explain(Exception exception)
    {
        var root = exception.GetBaseException();
        return root.Message == exception.Message
            ? exception.Message
            : $"{exception.Message} ({root.Message})";
    }

    protected INeuron ResolveNeuron(string neuronId)
    {
        if (neuronId.StartsWith("task-", StringComparison.OrdinalIgnoreCase))
        {
            return Grains.GetGrain<INeuron>(neuronId);
        }

        return neuronId switch
        {
            "aspire-main" => Grains.GetGrain<IAspireNeuron>(neuronId),
            "context-main" => Grains.GetGrain<INeuron>(neuronId),
            "chart-main" => Grains.GetGrain<IDataVisualizationNeuron>(neuronId),
            _ when neuronId.StartsWith("chart-", StringComparison.OrdinalIgnoreCase) => Grains.GetGrain<IChartNeuron>(neuronId),
            "db-main" => Grains.GetGrain<IDbSupportNeuron>(neuronId),
            "ino-main" => Grains.GetGrain<IInoNeuron>(neuronId),
            "llm-main" => Grains.GetGrain<ILlmNeuron>(neuronId),
            "status-main" => Grains.GetGrain<ISystemStatus>(neuronId),
            _ => Grains.GetGrain<IIngressNeuron>(neuronId)
        };
    }

    protected static JsonElement ReadObject(JsonElement element, string propertyName)
    {
        var value = ReadElement(element, propertyName);
        return value.HasValue && value.Value.ValueKind == JsonValueKind.Object ? value.Value : default;
    }

    protected static string? ReadString(JsonElement element, string propertyName)
    {
        var value = ReadElement(element, propertyName);
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    protected static JsonElement? ReadElement(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) ||
                string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }
}
