using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Brain.Abstractions.Identity;
using Brain.Product.Abstractions.Activities;
using Brain.Product.Abstractions.Operations;

namespace DigitalBrain.ProductHost.Catalog;

public abstract class ProductJsonAdapter : IProductOperationAdapter
{
    protected ProductJsonAdapter(
        ProductOperationDescriptor descriptor,
        JsonTypeInfo inputJsonType,
        JsonTypeInfo terminalResultJsonType)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        InputContract = ProductJsonContract.Create(
            "input",
            descriptor.InputSchema,
            inputJsonType);
        TerminalResultContract = ProductJsonContract.Create(
            "terminal result",
            descriptor.TerminalResultSchema,
            terminalResultJsonType);
        Operations = [descriptor];
    }

    public ProductOperationDescriptor Descriptor { get; }

    public IReadOnlyList<ProductOperationDescriptor> Operations { get; }

    internal ProductJsonContract InputContract { get; }

    internal ProductJsonContract TerminalResultContract { get; }

    public abstract Task<ProductActivityReceipt> InvokeAsync(
        OperationId operation,
        JsonElement input,
        ProductInvocationContext context,
        CancellationToken cancellationToken);

    public abstract Task<ProductActivityProjection> ObserveAsync(
        BrainActivityId activity,
        ProductInvocationContext context,
        CancellationToken cancellationToken);
}

public sealed class ProductJsonAdapter<TInput, TResult> : ProductJsonAdapter
    where TInput : class
    where TResult : class
{
    private readonly Func<TInput, ProductInvocationContext, CancellationToken, Task<ProductActivityReceipt>> _invoke;
    private readonly Func<BrainActivityId, ProductInvocationContext, CancellationToken, Task<ProductActivityProjection>> _observe;
    private readonly JsonTypeInfo<TInput> _inputJsonType;

    public ProductJsonAdapter(
        ProductOperationDescriptor descriptor,
        JsonTypeInfo<TInput> inputJsonType,
        JsonTypeInfo<TResult> terminalResultJsonType,
        Func<TInput, ProductInvocationContext, CancellationToken, Task<ProductActivityReceipt>> invoke,
        Func<BrainActivityId, ProductInvocationContext, CancellationToken, Task<ProductActivityProjection>> observe)
        : base(descriptor, inputJsonType, terminalResultJsonType)
    {
        _inputJsonType = inputJsonType;
        _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        _observe = observe ?? throw new ArgumentNullException(nameof(observe));
    }

    public override Task<ProductActivityReceipt> InvokeAsync(
        OperationId operation,
        JsonElement input,
        ProductInvocationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (operation != Descriptor.Operation.Id)
        {
            throw new ProductOperationNotAvailableException(operation);
        }

        TInput? bound;
        try
        {
            if (input.ValueKind == JsonValueKind.Undefined)
            {
                throw new ProductOperationInputException("The operation input must contain JSON.");
            }

            bound = JsonSerializer.Deserialize(input, _inputJsonType);
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

        if (bound is null)
        {
            throw new ProductOperationInputException("The operation input cannot be null.");
        }

        return _invoke(bound, context, cancellationToken);
    }

    public override Task<ProductActivityProjection> ObserveAsync(
        BrainActivityId activity,
        ProductInvocationContext context,
        CancellationToken cancellationToken)
        => _observe(activity, context, cancellationToken);
}

internal sealed class ProductJsonContract
{
    private ProductJsonContract(JsonTypeInfo metadata)
    {
        Metadata = metadata;
    }

    internal JsonTypeInfo Metadata { get; }

    internal static ProductJsonContract Create(string kind, string schema, JsonTypeInfo metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(metadata);
        ValidateClosedMetadata(kind, metadata, []);
        ValidateSchema(kind, schema, metadata);
        return new ProductJsonContract(metadata);
    }

    private static void ValidateClosedMetadata(
        string kind,
        JsonTypeInfo metadata,
        HashSet<Type> visited)
    {
        if (!visited.Add(metadata.Type))
        {
            return;
        }

        var options = metadata.Options;
        if (options.AllowDuplicateProperties
            || !options.RespectRequiredConstructorParameters
            || !options.RespectNullableAnnotations
            || options.PropertyNameCaseInsensitive
            || options.Converters.Count != 0
            || options.TypeClassifiers.Count != 0)
        {
            throw Invalid(kind, "must use strict source-generated serializer options");
        }

        if (metadata.Kind != JsonTypeInfoKind.Object
            || metadata.Type.IsValueType
            || metadata.Type == typeof(string)
            || EffectiveUnmappedHandling(metadata) != JsonUnmappedMemberHandling.Disallow
            || metadata.PolymorphismOptions is not null
            || metadata.UnionCases.Count != 0)
        {
            throw Invalid(kind, "must describe one non-polymorphic closed object contract");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in metadata.Properties)
        {
            if (!names.Add(property.Name)
                || property.IsExtensionData
                || property.CustomConverter is not null
                || IsOpenType(property.PropertyType))
            {
                throw Invalid(kind, $"contains an open or ambiguous property '{property.Name}'");
            }

            var propertyMetadata = GetMetadata(kind, metadata, property.PropertyType);
            ValidatePropertyMetadata(kind, property.Name, propertyMetadata, visited);
        }
    }

    private static void ValidatePropertyMetadata(
        string kind,
        string propertyName,
        JsonTypeInfo metadata,
        HashSet<Type> visited)
    {
        switch (metadata.Kind)
        {
            case JsonTypeInfoKind.Object:
                ValidateClosedMetadata(kind, metadata, visited);
                break;
            case JsonTypeInfoKind.Enumerable:
                if (metadata.ElementType is null || IsOpenType(metadata.ElementType))
                {
                    throw Invalid(kind, $"contains an open collection property '{propertyName}'");
                }

                ValidatePropertyMetadata(
                    kind,
                    propertyName,
                    GetMetadata(kind, metadata, metadata.ElementType),
                    visited);
                break;
            case JsonTypeInfoKind.Dictionary:
                throw Invalid(kind, $"contains an open dictionary property '{propertyName}'");
            case JsonTypeInfoKind.None:
                if (!IsClosedScalar(metadata.Type))
                {
                    throw Invalid(kind, $"contains a custom-converted or open property '{propertyName}'");
                }

                break;
            default:
                throw Invalid(kind, $"contains unsupported metadata for property '{propertyName}'");
        }
    }

    private static JsonTypeInfo GetMetadata(string kind, JsonTypeInfo owner, Type propertyType)
    {
        try
        {
            return owner.Options.GetTypeInfo(propertyType);
        }
        catch (NotSupportedException exception)
        {
            throw new ProductOperationCatalogConfigurationException(
                $"The {kind} JSON metadata is incomplete for '{propertyType.Name}'.",
                exception);
        }
    }

    private static JsonUnmappedMemberHandling EffectiveUnmappedHandling(JsonTypeInfo metadata)
        => metadata.UnmappedMemberHandling ?? metadata.Options.UnmappedMemberHandling;

    private static bool IsOpenType(Type type)
    {
        var candidate = Nullable.GetUnderlyingType(type) ?? type;
        if (candidate == typeof(object)
            || candidate == typeof(JsonElement)
            || candidate == typeof(JsonDocument)
            || typeof(JsonNode).IsAssignableFrom(candidate))
        {
            return true;
        }

        return candidate.IsGenericType && candidate.GetGenericArguments().Any(IsOpenType);
    }

    private static bool IsClosedScalar(Type type)
    {
        var candidate = Nullable.GetUnderlyingType(type) ?? type;
        return candidate.IsEnum
            || candidate == typeof(string)
            || candidate == typeof(bool)
            || candidate == typeof(char)
            || candidate == typeof(byte)
            || candidate == typeof(sbyte)
            || candidate == typeof(short)
            || candidate == typeof(ushort)
            || candidate == typeof(int)
            || candidate == typeof(uint)
            || candidate == typeof(long)
            || candidate == typeof(ulong)
            || candidate == typeof(float)
            || candidate == typeof(double)
            || candidate == typeof(decimal)
            || candidate == typeof(Guid)
            || candidate == typeof(DateTime)
            || candidate == typeof(DateTimeOffset)
            || candidate == typeof(TimeSpan)
            || candidate == typeof(Uri);
    }

    private static void ValidateSchema(string kind, string schema, JsonTypeInfo metadata)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(schema);
        }
        catch (JsonException exception)
        {
            throw new ProductOperationCatalogConfigurationException(
                $"The {kind} schema is not valid JSON.",
                exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid(kind, "schema must be a JSON object");
            }

            EnsureUniqueObjectProperties(kind, root);
            if (!TryGetSingleProperty(root, "type", out var type)
                || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), "object", StringComparison.Ordinal)
                || !TryGetSingleProperty(root, "additionalProperties", out var additional)
                || additional.ValueKind is not JsonValueKind.False
                || !TryGetSingleProperty(root, "properties", out var properties)
                || properties.ValueKind != JsonValueKind.Object
                || !TryGetSingleProperty(root, "required", out var required)
                || required.ValueKind != JsonValueKind.Array)
            {
                throw Invalid(kind, "schema must declare a closed object with properties and required members");
            }

            EnsureUniqueObjectProperties(kind, properties);
            var metadataProperties = metadata.Properties.ToDictionary(
                static property => property.Name,
                StringComparer.Ordinal);
            var schemaProperties = properties.EnumerateObject().ToArray();
            if (!schemaProperties.Select(static property => property.Name).ToHashSet(StringComparer.Ordinal)
                .SetEquals(metadataProperties.Keys))
            {
                throw Invalid(kind, "schema properties do not match generated CLR metadata");
            }

            foreach (var schemaProperty in schemaProperties)
            {
                ValidateSchemaProperty(
                    kind,
                    schemaProperty.Value,
                    metadataProperties[schemaProperty.Name].PropertyType);
            }

            var requiredNames = ReadRequiredNames(kind, required);
            var metadataRequired = metadata.Properties
                .Where(property => property.IsRequired
                    || property.AssociatedParameter is { HasDefaultValue: false })
                .Select(static property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
            if (!requiredNames.SetEquals(metadataRequired))
            {
                throw Invalid(kind, "schema required members do not match generated CLR metadata");
            }
        }
    }

    private static void ValidateSchemaProperty(string kind, JsonElement schema, Type propertyType)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(kind, "each schema property must be an object");
        }

        EnsureUniqueObjectProperties(kind, schema);
        if (!TryGetSingleProperty(schema, "type", out var schemaType)
            || schemaType.ValueKind != JsonValueKind.String
            || !string.Equals(schemaType.GetString(), ExpectedSchemaType(propertyType), StringComparison.Ordinal))
        {
            throw Invalid(kind, "a schema property type does not match generated CLR metadata");
        }
    }

    private static string ExpectedSchemaType(Type propertyType)
    {
        var candidate = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (candidate == typeof(string)
            || candidate == typeof(char)
            || candidate == typeof(Guid)
            || candidate == typeof(DateTime)
            || candidate == typeof(DateTimeOffset)
            || candidate == typeof(TimeSpan)
            || candidate == typeof(Uri))
        {
            return "string";
        }

        if (candidate == typeof(bool))
        {
            return "boolean";
        }

        if (candidate.IsEnum
            || candidate == typeof(byte)
            || candidate == typeof(sbyte)
            || candidate == typeof(short)
            || candidate == typeof(ushort)
            || candidate == typeof(int)
            || candidate == typeof(uint)
            || candidate == typeof(long)
            || candidate == typeof(ulong))
        {
            return "integer";
        }

        if (candidate == typeof(float)
            || candidate == typeof(double)
            || candidate == typeof(decimal))
        {
            return "number";
        }

        return typeof(System.Collections.IEnumerable).IsAssignableFrom(candidate)
            ? "array"
            : "object";
    }

    private static HashSet<string> ReadRequiredNames(string kind, JsonElement required)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in required.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(item.GetString())
                || !names.Add(item.GetString()!))
            {
                throw Invalid(kind, "schema required members must be unique non-empty strings");
            }
        }

        return names;
    }

    private static void EnsureUniqueObjectProperties(string kind, JsonElement value)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (value.EnumerateObject().Any(property => !names.Add(property.Name)))
        {
            throw Invalid(kind, "schema cannot contain duplicate object properties");
        }
    }

    private static bool TryGetSingleProperty(
        JsonElement value,
        string propertyName,
        out JsonElement propertyValue)
    {
        var matches = value.EnumerateObject()
            .Where(property => string.Equals(property.Name, propertyName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 1)
        {
            propertyValue = matches[0].Value;
            return true;
        }

        propertyValue = default;
        return false;
    }

    private static ProductOperationCatalogConfigurationException Invalid(string kind, string requirement)
        => new($"The {kind} JSON contract {requirement}.");
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
