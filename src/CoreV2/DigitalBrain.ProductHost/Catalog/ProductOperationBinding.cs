using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Brain.Abstractions.Activities;
using Brain.Abstractions.Identity;
using Brain.Product.Abstractions.Activities;
using Brain.Product.Abstractions.Operations;

namespace DigitalBrain.ProductHost.Catalog;

public sealed record ProductOperationObservation<TResult>
    where TResult : class
{
    public ProductOperationObservation(ActivityView activity, JsonElement? progress, TResult? result)
    {
        ArgumentNullException.ThrowIfNull(activity);
        Activity = activity;
        Progress = progress?.Clone();
        Result = result;
    }

    public ActivityView Activity { get; }

    public JsonElement? Progress { get; }

    public TResult? Result { get; }
}

public sealed class ProductOperationBinding : IProductOperationAdapter
{
    private readonly IBindingState _state;

    private ProductOperationBinding(
        ProductOperationDescriptor descriptor,
        JsonTypeInfo inputJsonType,
        JsonTypeInfo terminalResultJsonType,
        IBindingState state)
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
        _state = state;
        Operations = [descriptor];
    }

    public ProductOperationDescriptor Descriptor { get; }

    public IReadOnlyList<ProductOperationDescriptor> Operations { get; }

    internal ProductJsonContract InputContract { get; }

    internal ProductJsonContract TerminalResultContract { get; }

    public static ProductOperationBinding Create<TInput, TResult>(
        ProductOperationDescriptor descriptor,
        JsonTypeInfo<TInput> inputJsonType,
        JsonTypeInfo<TResult> terminalResultJsonType,
        Func<TInput, ProductInvocationContext, CancellationToken, Task<ProductActivityReceipt>> invoke,
        Func<BrainActivityId, ProductInvocationContext, CancellationToken, Task<ProductOperationObservation<TResult>>> observe)
        where TInput : class
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(inputJsonType);
        ArgumentNullException.ThrowIfNull(terminalResultJsonType);
        ArgumentNullException.ThrowIfNull(invoke);
        ArgumentNullException.ThrowIfNull(observe);
        return new ProductOperationBinding(
            descriptor,
            inputJsonType,
            terminalResultJsonType,
            new TypedBindingState<TInput, TResult>(
                inputJsonType,
                terminalResultJsonType,
                invoke,
                observe));
    }

    internal Task<ProductActivityReceipt> InvokeAsync(
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

        try
        {
            InputContract.Validate(input);
        }
        catch (JsonException exception)
        {
            throw new ProductOperationInputException(
                "The operation input does not match its declared JSON contract.",
                exception);
        }

        return _state.InvokeAsync(input, context, cancellationToken);
    }

    internal async Task<ProductActivityProjection> ObserveAsync(
        BrainActivityId activity,
        ProductInvocationContext context,
        CancellationToken cancellationToken)
    {
        var projection = await _state.ObserveAsync(activity, context, cancellationToken)
            .ConfigureAwait(false);
        if (projection.Result is { } result)
        {
            try
            {
                TerminalResultContract.Validate(result);
            }
            catch (JsonException exception)
            {
                throw new ProductOperationResultException(
                    "The terminal result does not match its declared JSON contract.",
                    exception);
            }
        }

        return projection;
    }

    Task<ProductActivityReceipt> IProductOperationAdapter.InvokeAsync(
        OperationId operation,
        JsonElement input,
        ProductInvocationContext context,
        CancellationToken cancellationToken)
        => InvokeAsync(operation, input, context, cancellationToken);

    Task<ProductActivityProjection> IProductOperationAdapter.ObserveAsync(
        BrainActivityId activity,
        ProductInvocationContext context,
        CancellationToken cancellationToken)
        => ObserveAsync(activity, context, cancellationToken);

    private interface IBindingState
    {
        Task<ProductActivityReceipt> InvokeAsync(
            JsonElement input,
            ProductInvocationContext context,
            CancellationToken cancellationToken);

        Task<ProductActivityProjection> ObserveAsync(
            BrainActivityId activity,
            ProductInvocationContext context,
            CancellationToken cancellationToken);
    }

    private sealed class TypedBindingState<TInput, TResult>(
        JsonTypeInfo<TInput> inputJsonType,
        JsonTypeInfo<TResult> terminalResultJsonType,
        Func<TInput, ProductInvocationContext, CancellationToken, Task<ProductActivityReceipt>> invoke,
        Func<BrainActivityId, ProductInvocationContext, CancellationToken, Task<ProductOperationObservation<TResult>>> observe)
        : IBindingState
        where TInput : class
        where TResult : class
    {
        public Task<ProductActivityReceipt> InvokeAsync(
            JsonElement input,
            ProductInvocationContext context,
            CancellationToken cancellationToken)
        {
            TInput? bound;
            try
            {
                if (input.ValueKind == JsonValueKind.Undefined)
                {
                    throw new ProductOperationInputException("The operation input must contain JSON.");
                }

                bound = JsonSerializer.Deserialize(input, inputJsonType);
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

            return invoke(bound, context, cancellationToken);
        }

        public async Task<ProductActivityProjection> ObserveAsync(
            BrainActivityId activity,
            ProductInvocationContext context,
            CancellationToken cancellationToken)
        {
            var observation = await observe(activity, context, cancellationToken).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(observation);

            JsonElement? result = null;
            if (observation.Result is not null)
            {
                try
                {
                    var serialized = JsonSerializer.SerializeToElement(
                        observation.Result,
                        terminalResultJsonType);
                    result = serialized;
                }
                catch (JsonException exception)
                {
                    throw new ProductOperationResultException(
                        "The terminal result does not match its declared JSON contract.",
                        exception);
                }
                catch (NotSupportedException exception)
                {
                    throw new ProductOperationResultException(
                        "The terminal result type is not supported by its declared JSON metadata.",
                        exception);
                }
            }

            return new ProductActivityProjection(
                observation.Activity,
                observation.Progress,
                result);
        }
    }
}

internal sealed class ProductJsonContract
{
    private readonly string _kind;
    private readonly JsonElement _schema;

    private ProductJsonContract(string kind, JsonTypeInfo metadata, JsonElement schema)
    {
        _kind = kind;
        Metadata = metadata;
        _schema = schema;
    }

    internal JsonTypeInfo Metadata { get; }

    internal static ProductJsonContract Create(string kind, string schema, JsonTypeInfo metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(metadata);
        ValidateStrictOptions(kind, metadata.Options);
        if (metadata.Kind != JsonTypeInfoKind.Object
            || metadata.Type.IsValueType
            || metadata.Type == typeof(string))
        {
            throw Invalid(kind, "root must be one closed reference-type object contract");
        }

        ValidateClosedMetadata(kind, metadata, []);

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
            var root = document.RootElement.Clone();
            ValidateSchemaNode(kind, root, metadata, allowsNull: false);
            return new ProductJsonContract(kind, metadata, root);
        }
    }

    internal void Validate(JsonElement value)
        => ValidateRuntimeValue(_kind, _schema, Metadata, value, allowsNull: false);

    private static void ValidateStrictOptions(string kind, JsonSerializerOptions options)
    {
        if (options.AllowDuplicateProperties
            || !options.RespectRequiredConstructorParameters
            || !options.RespectNullableAnnotations
            || options.PropertyNameCaseInsensitive
            || options.Converters.Count != 0
            || options.TypeClassifiers.Count != 0)
        {
            throw Invalid(kind, "must use strict source-generated serializer options");
        }
    }

    private static void ValidateClosedMetadata(
        string kind,
        JsonTypeInfo metadata,
        HashSet<Type> visited)
    {
        if (!visited.Add(metadata.Type))
        {
            throw Invalid(kind, "cannot contain recursive CLR contracts");
        }

        try
        {
            switch (metadata.Kind)
            {
                case JsonTypeInfoKind.Object:
                    ValidateClosedObjectMetadata(kind, metadata, visited);
                    break;
                case JsonTypeInfoKind.Enumerable:
                    if (metadata.ElementType is null || IsOpenType(metadata.ElementType))
                    {
                        throw Invalid(kind, "contains an open collection contract");
                    }

                    ValidateClosedMetadata(
                        kind,
                        GetMetadata(kind, metadata, metadata.ElementType),
                        visited);
                    break;
                case JsonTypeInfoKind.Dictionary:
                    throw Invalid(kind, "cannot contain dictionary contracts");
                case JsonTypeInfoKind.None:
                    if (!IsClosedScalar(metadata.Type))
                    {
                        throw Invalid(kind, "contains a custom-converted or open contract");
                    }

                    break;
                default:
                    throw Invalid(kind, "contains unsupported generated metadata");
            }
        }
        finally
        {
            visited.Remove(metadata.Type);
        }
    }

    private static void ValidateClosedObjectMetadata(
        string kind,
        JsonTypeInfo metadata,
        HashSet<Type> visited)
    {
        if (metadata.Type.IsValueType
            || metadata.Type == typeof(string)
            || EffectiveUnmappedHandling(metadata) != JsonUnmappedMemberHandling.Disallow
            || metadata.PolymorphismOptions is not null
            || metadata.UnionCases.Count != 0)
        {
            throw Invalid(kind, "must describe non-polymorphic closed object contracts");
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

            ValidateClosedMetadata(
                kind,
                GetMetadata(kind, metadata, property.PropertyType),
                visited);
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

    private static void ValidateSchemaNode(
        string kind,
        JsonElement schema,
        JsonTypeInfo metadata,
        bool allowsNull)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(kind, "schema nodes must be JSON objects");
        }

        EnsureUniqueObjectProperties(kind, schema);
        var expectedType = ExpectedSchemaType(metadata.Type);
        ValidateTypeKeyword(kind, schema, expectedType, allowsNull);
        switch (metadata.Kind)
        {
            case JsonTypeInfoKind.Object:
                ValidateObjectSchema(kind, schema, metadata);
                break;
            case JsonTypeInfoKind.Enumerable:
                ValidateEnumerableSchema(kind, schema, metadata);
                break;
            case JsonTypeInfoKind.None:
                EnsureAllowedKeywords(kind, schema, "type");
                break;
            default:
                throw Invalid(kind, "schema metadata kind is unsupported");
        }
    }

    private static void ValidateObjectSchema(string kind, JsonElement schema, JsonTypeInfo metadata)
    {
        EnsureAllowedKeywords(kind, schema, "type", "additionalProperties", "properties", "required");
        if (!TryGetSingleProperty(schema, "additionalProperties", out var additional)
            || additional.ValueKind is not JsonValueKind.False
            || !TryGetSingleProperty(schema, "properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object
            || !TryGetSingleProperty(schema, "required", out var required)
            || required.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(kind, "object schemas must be closed and declare properties and required members");
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
            var property = metadataProperties[schemaProperty.Name];
            ValidateSchemaNode(
                kind,
                schemaProperty.Value,
                GetMetadata(kind, metadata, property.PropertyType),
                AllowsNull(property));
        }

        var requiredNames = ReadRequiredNames(kind, required);
        var metadataRequired = metadata.Properties
            .Where(IsRequired)
            .Select(static property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!requiredNames.SetEquals(metadataRequired))
        {
            throw Invalid(kind, "schema required members do not match generated CLR metadata");
        }
    }

    private static void ValidateEnumerableSchema(string kind, JsonElement schema, JsonTypeInfo metadata)
    {
        EnsureAllowedKeywords(kind, schema, "type", "items");
        if (!TryGetSingleProperty(schema, "items", out var items)
            || metadata.ElementType is null)
        {
            throw Invalid(kind, "array schemas must declare generated item metadata");
        }

        ValidateSchemaNode(
            kind,
            items,
            GetMetadata(kind, metadata, metadata.ElementType),
            Nullable.GetUnderlyingType(metadata.ElementType) is not null);
    }

    private static void ValidateTypeKeyword(
        string kind,
        JsonElement schema,
        string expectedType,
        bool allowsNull)
    {
        if (!TryGetSingleProperty(schema, "type", out var type))
        {
            throw Invalid(kind, "schema nodes must declare an exact type");
        }

        if (!allowsNull)
        {
            if (type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), expectedType, StringComparison.Ordinal))
            {
                throw Invalid(kind, "schema type does not match generated CLR metadata");
            }

            return;
        }

        if (type.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(kind, "nullable CLR members must declare both their value type and null");
        }

        var values = type.EnumerateArray().ToArray();
        if (values.Length != 2
            || values.Any(static value => value.ValueKind != JsonValueKind.String)
            || !values.Select(static value => value.GetString()).ToHashSet(StringComparer.Ordinal)
                .SetEquals([expectedType, "null"]))
        {
            throw Invalid(kind, "nullable schema types must contain exactly the CLR type and null");
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

    private static bool AllowsNull(JsonPropertyInfo property)
        => Nullable.GetUnderlyingType(property.PropertyType) is not null
            || property.IsGetNullable
            || property.IsSetNullable;

    private static bool IsRequired(JsonPropertyInfo property)
        => property.IsRequired || property.AssociatedParameter is { HasDefaultValue: false };

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

    private static void EnsureAllowedKeywords(
        string kind,
        JsonElement schema,
        params string[] allowedKeywords)
    {
        var allowed = allowedKeywords.ToHashSet(StringComparer.Ordinal);
        if (schema.EnumerateObject().Any(property => !allowed.Contains(property.Name)))
        {
            throw Invalid(kind, "schema contains a constraint the typed binding does not enforce");
        }
    }

    private static void ValidateRuntimeValue(
        string kind,
        JsonElement schema,
        JsonTypeInfo metadata,
        JsonElement value,
        bool allowsNull)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (allowsNull)
            {
                return;
            }

            throw RuntimeInvalid(kind, "contains null where its generated metadata is non-nullable");
        }

        switch (metadata.Kind)
        {
            case JsonTypeInfoKind.Object:
                ValidateRuntimeObject(kind, schema, metadata, value);
                break;
            case JsonTypeInfoKind.Enumerable:
                ValidateRuntimeArray(kind, schema, metadata, value);
                break;
            case JsonTypeInfoKind.None:
                ValidateRuntimeScalar(kind, metadata.Type, value);
                break;
            default:
                throw RuntimeInvalid(kind, "uses unsupported generated metadata");
        }
    }

    private static void ValidateRuntimeObject(
        string kind,
        JsonElement schema,
        JsonTypeInfo metadata,
        JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw RuntimeInvalid(kind, "must contain an object");
        }

        var values = value.EnumerateObject().ToArray();
        if (values.Select(static property => property.Name).Distinct(StringComparer.Ordinal).Count()
            != values.Length)
        {
            throw RuntimeInvalid(kind, "cannot contain duplicate object properties");
        }

        _ = TryGetSingleProperty(schema, "properties", out var schemaProperties);
        _ = TryGetSingleProperty(schema, "required", out var required);
        var requiredNames = required.EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var presentNames = values.Select(static property => property.Name).ToHashSet(StringComparer.Ordinal);
        if (!requiredNames.IsSubsetOf(presentNames))
        {
            throw RuntimeInvalid(kind, "is missing a required object property");
        }

        var metadataProperties = metadata.Properties.ToDictionary(
            static property => property.Name,
            StringComparer.Ordinal);
        foreach (var propertyValue in values)
        {
            if (!metadataProperties.TryGetValue(propertyValue.Name, out var property)
                || !TryGetSingleProperty(schemaProperties, propertyValue.Name, out var propertySchema))
            {
                throw RuntimeInvalid(kind, "contains an undeclared object property");
            }

            ValidateRuntimeValue(
                kind,
                propertySchema,
                GetMetadata(kind, metadata, property.PropertyType),
                propertyValue.Value,
                AllowsNull(property));
        }
    }

    private static void ValidateRuntimeArray(
        string kind,
        JsonElement schema,
        JsonTypeInfo metadata,
        JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || metadata.ElementType is null)
        {
            throw RuntimeInvalid(kind, "must contain an array");
        }

        _ = TryGetSingleProperty(schema, "items", out var items);
        var elementMetadata = GetMetadata(kind, metadata, metadata.ElementType);
        var elementAllowsNull = Nullable.GetUnderlyingType(metadata.ElementType) is not null;
        foreach (var element in value.EnumerateArray())
        {
            ValidateRuntimeValue(kind, items, elementMetadata, element, elementAllowsNull);
        }
    }

    private static void ValidateRuntimeScalar(string kind, Type type, JsonElement value)
    {
        var expected = ExpectedSchemaType(type);
        var valid = expected switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => value.ValueKind == JsonValueKind.Number
                && (value.TryGetInt64(out _) || value.TryGetUInt64(out _)),
            "number" => value.ValueKind == JsonValueKind.Number,
            _ => false,
        };
        if (!valid)
        {
            throw RuntimeInvalid(kind, "contains a value with the wrong JSON type");
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

    private static JsonException RuntimeInvalid(string kind, string requirement)
        => new($"The {kind} JSON value {requirement}.");
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

public sealed class ProductOperationResultException : Exception
{
    public ProductOperationResultException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
