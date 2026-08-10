using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

public static class SynapseCapabilityTool
{
    private const string CommandIdProperty = "commandId";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };


    

    public static Synapse BindModelArguments(
        Type requestType,
        string contractId,
        IEnumerable<KeyValuePair<string, object?>> arguments)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        ArgumentNullException.ThrowIfNull(arguments);

        var node = new JsonObject();
        foreach (var (key, value) in arguments)
        {
            node[key] = value switch
            {
                null => null,
                JsonElement element => JsonNode.Parse(element.GetRawText()),
                JsonNode jsonNode => jsonNode,
                _ => JsonSerializer.SerializeToNode(value, SerializerOptions),
            };
        }

        DeriveIdentitiesFromStableNames(requestType, node);

        if (requestType.GetProperty(nameof(CommandId))?.PropertyType == typeof(CommandId))
        {
            node[CommandIdProperty] = new JsonObject { ["value"] = CommandId.New().Value };
        }

        var request = JsonSerializer.Deserialize(node, requestType, SerializerOptions)
            ?? throw new InvalidOperationException(
                $"Model arguments could not be bound to '{contractId}'.");
        if (request is not Synapse synapse)
        {
            throw new InvalidOperationException(
                $"Bound request for '{contractId}' is not a Synapse.");
        }

        return synapse;
    }

    // Models name things; identity properties are GUIDs. A stable name maps to a
    // deterministic GUID so the same name always addresses the same identity.
    private static void DeriveIdentitiesFromStableNames(Type requestType, JsonObject node)
    {
        foreach (var property in requestType.GetProperties())
        {
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (propertyType != typeof(Guid))
            {
                continue;
            }

            var key = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (node.TryGetPropertyValue(key, out var value)
                && value is JsonValue named
                && named.TryGetValue<string>(out var text)
                && !Guid.TryParse(text, out _)
                && !string.IsNullOrWhiteSpace(text))
            {
                node[key] = new Guid(
                    System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));
            }
        }
    }


    internal static readonly TimeSpan ToolResponseWait = DeliveryPolicy.DeliveryAttemptTimeout * 3;


}
