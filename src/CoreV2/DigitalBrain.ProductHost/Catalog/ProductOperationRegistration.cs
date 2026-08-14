using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Brain.Abstractions.Operations;
using Brain.Product.Abstractions.Operations;

namespace DigitalBrain.ProductHost.Catalog;

public sealed class ProductOperationRegistration
{
    public ProductOperationRegistration(
        OperationDescriptor declaredOperation,
        IProductOperationAdapter adapter,
        JsonTypeInfo inputJsonType,
        JsonTypeInfo terminalResultJsonType,
        ProductOperationAccessPolicy accessPolicy)
    {
        ArgumentNullException.ThrowIfNull(declaredOperation);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(adapter.Operations);
        ArgumentNullException.ThrowIfNull(accessPolicy);

        var matchingDescriptors = adapter.Operations
            .Where(candidate => candidate.Operation.Id == declaredOperation.Id)
            .ToArray();
        if (matchingDescriptors.Length != 1)
        {
            throw new ProductOperationCatalogConfigurationException(
                $"Operation '{declaredOperation.Id}' must have exactly one explicit product descriptor.");
        }

        var productDescriptor = matchingDescriptors[0];
        if (productDescriptor.Operation != declaredOperation)
        {
            throw new ProductOperationCatalogConfigurationException(
                $"Operation '{declaredOperation.Id}' does not match its adapter descriptor.");
        }

        ValidateSchema(productDescriptor.InputSchema, "input");
        ValidateSchema(productDescriptor.TerminalResultSchema, "terminal result");

        DeclaredOperation = declaredOperation;
        Adapter = adapter;
        Descriptor = productDescriptor;
        Json = new ProductJsonAdapter(inputJsonType, terminalResultJsonType);
        AccessPolicy = accessPolicy;
    }

    public OperationDescriptor DeclaredOperation { get; }

    public IProductOperationAdapter Adapter { get; }

    public ProductOperationDescriptor Descriptor { get; }

    public ProductJsonAdapter Json { get; }

    public ProductOperationAccessPolicy AccessPolicy { get; }

    private static void ValidateSchema(string schema, string kind)
    {
        try
        {
            using var document = JsonDocument.Parse(schema);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ProductOperationCatalogConfigurationException(
                    $"The {kind} schema must be a JSON object.");
            }
        }
        catch (JsonException exception)
        {
            throw new ProductOperationCatalogConfigurationException(
                $"The {kind} schema is not valid JSON.",
                exception);
        }
    }
}

public sealed class ProductOperationCatalogConfigurationException : Exception
{
    public ProductOperationCatalogConfigurationException(string message)
        : base(message)
    {
    }

    public ProductOperationCatalogConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
