using System.Globalization;
using System.Text.Json.Nodes;
using Xunit;

namespace DigitalBrain.ProductTests;

internal static class LiveProductJson
{
    internal static JsonNode Parse(string output)
    {
        var lines = output.Split(
            ["\r\n", "\n"],
            StringSplitOptions.None);
        var firstJsonLine = Array.FindIndex(
            lines,
            line =>
            {
                var trimmed = line.TrimStart();
                return trimmed.StartsWith('{', StringComparison.Ordinal)
                    || trimmed.StartsWith('[', StringComparison.Ordinal);
            });

        if (firstJsonLine < 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"The Aspire CLI did not return JSON.{Environment.NewLine}{output}");
        }

        return JsonNode.Parse(string.Join(Environment.NewLine, lines[firstJsonLine..]))
            ?? throw new Xunit.Sdk.XunitException("The Aspire CLI returned JSON null.");
    }

    internal static JsonArray RequiredArray(JsonNode? node, string property)
        => node?[property] as JsonArray
            ?? throw new Xunit.Sdk.XunitException(
                $"Expected JSON array '{property}' in {node?.ToJsonString()}.");

    internal static JsonObject RequiredObject(JsonNode? node, string property)
        => node?[property] as JsonObject
            ?? throw new Xunit.Sdk.XunitException(
                $"Expected JSON object '{property}' in {node?.ToJsonString()}.");

    internal static string RequiredString(JsonNode? node, string property)
        => OptionalString(node, property)
            ?? throw new Xunit.Sdk.XunitException(
                $"Expected JSON string '{property}' in {node?.ToJsonString()}.");

    internal static string? OptionalString(JsonNode? node, string property)
    {
        var value = node?[property];
        if (value is null)
        {
            return null;
        }

        return value is JsonValue jsonValue
               && jsonValue.TryGetValue<string>(out var text)
            ? text
            : value.ToJsonString();
    }

    internal static long RequiredLong(JsonNode? node, string property)
    {
        var value = node?[property];
        if (value is JsonValue jsonValue
            && jsonValue.TryGetValue<long>(out var number))
        {
            return number;
        }

        if (long.TryParse(OptionalString(node, property), CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw new Xunit.Sdk.XunitException(
            $"Expected JSON integer '{property}' in {node?.ToJsonString()}.");
    }
}
