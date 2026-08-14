using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace DigitalBrain.ProductHost.Catalog;

public sealed class ProductJsonAdapter
{
    public ProductJsonAdapter(JsonTypeInfo input, JsonTypeInfo terminalResult)
    {
        Input = RequireClosedObjectMetadata(input, nameof(input));
        TerminalResult = RequireClosedObjectMetadata(terminalResult, nameof(terminalResult));
    }

    public JsonTypeInfo Input { get; }

    public JsonTypeInfo TerminalResult { get; }

    public JsonElement ValidateAndCloneInput(JsonElement input)
    {
        if (input.ValueKind == JsonValueKind.Undefined)
        {
            throw new ProductOperationInputException("The operation input must contain JSON.");
        }

        try
        {
            var bound = JsonSerializer.Deserialize(input, Input);
            if (bound is null)
            {
                throw new ProductOperationInputException("The operation input cannot be null.");
            }

            return input.Clone();
        }
        catch (JsonException exception)
        {
            throw new ProductOperationInputException(
                "The operation input does not match its declared JSON contract.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new ProductOperationInputException(
                "The operation input type is not supported by its declared JSON metadata.",
                exception);
        }
    }

    private static JsonTypeInfo RequireClosedObjectMetadata(JsonTypeInfo metadata, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(metadata, parameterName);
        if (metadata.Kind != JsonTypeInfoKind.Object
            || metadata.Type.IsValueType
            || metadata.Type == typeof(string))
        {
            throw new ProductOperationCatalogConfigurationException(
                $"JSON metadata '{parameterName}' must describe a reference-type object contract.");
        }

        if (metadata.Options.UnmappedMemberHandling != JsonUnmappedMemberHandling.Disallow)
        {
            throw new ProductOperationCatalogConfigurationException(
                $"JSON metadata '{parameterName}' must reject unmapped members.");
        }

        return metadata;
    }
}

public sealed class ProductOperationInputException : Exception
{
    public ProductOperationInputException(string message)
        : base(message)
    {
    }

    public ProductOperationInputException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
