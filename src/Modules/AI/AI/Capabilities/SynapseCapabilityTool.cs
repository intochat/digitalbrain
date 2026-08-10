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
        IEnumerable<KeyValuePair<string, object?>> arguments,
        OwnerId? owner = null)
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
        ParseNeuronIdStrings(requestType, node, owner);

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

    // Models speak identities the way get_neurons prints them — "timer:dev/default"
    // or "chat:main" — never as nested owner objects.
    private static void ParseNeuronIdStrings(Type requestType, JsonObject node, OwnerId? owner)
    {
        foreach (var property in requestType.GetProperties())
        {
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (propertyType != typeof(NeuronId))
            {
                continue;
            }

            var key = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!node.TryGetPropertyValue(key, out var value)
                || value is not JsonValue named
                || !named.TryGetValue<string>(out var text)
                || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var separator = text.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator == text.Length - 1)
            {
                throw new InvalidOperationException(
                    $"'{text}' is not a neuron identity. Write it as type:name or type:owner/name.");
            }

            var type = text[..separator];
            var rest = text[(separator + 1)..];
            var slash = rest.IndexOf('/', StringComparison.Ordinal);
            var (ownerPart, name) = slash > 0
                ? (rest[..slash], rest[(slash + 1)..])
                : (owner?.Value ?? throw new InvalidOperationException(
                    $"'{text}' has no owner and no ambient owner was provided."), rest);

            var identity = new NeuronId(type, new OwnerId(ownerPart), name);
            node[key] = new JsonObject
            {
                ["type"] = identity.Type,
                ["owner"] = new JsonObject { ["value"] = identity.Owner.Value },
                ["name"] = identity.Name,
            };
        }
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
