using System.Collections.ObjectModel;
using Brain.Abstractions.Identity;
using Brain.Core.Modules;
using Brain.Product.Abstractions.Activities;
using Brain.Product.Abstractions.Authority;
using Brain.Product.Abstractions.Operations;

namespace DigitalBrain.ProductHost.Catalog;

public sealed class ProductOperationCatalog
{
    private readonly IReadOnlyDictionary<string, ProductOperationRegistration> _registrations;
    private readonly ProductOperationPolicyFilter _policyFilter;

    public ProductOperationCatalog(
        IModuleRegistry moduleRegistry,
        ProductOperationPolicyFilter policyFilter,
        IReadOnlyCollection<ProductOperationRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        _policyFilter = policyFilter ?? throw new ArgumentNullException(nameof(policyFilter));
        ArgumentNullException.ThrowIfNull(registrations);

        var index = new SortedDictionary<string, ProductOperationRegistration>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            VerifyInstalledManifest(moduleRegistry, registration);
            if (!index.TryAdd(registration.DeclaredOperation.Id.Value, registration))
            {
                throw new ProductOperationCatalogConfigurationException(
                    $"Operation '{registration.DeclaredOperation.Id}' has more than one product registration.");
            }
        }

        _registrations = new ReadOnlyDictionary<string, ProductOperationRegistration>(index);
    }

    public Task<IReadOnlyList<ProductOperationDescriptor>> DiscoverAsync(
        BrainAccessGrant grant,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grant);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ProductOperationDescriptor> visible = _registrations.Values
            .Where(registration => _policyFilter.IsAvailable(grant, registration))
            .Select(static registration => registration.Descriptor)
            .ToArray();
        return Task.FromResult(visible);
    }

    public async Task<ProductActivityReceipt> InvokeAsync(
        OperationId operation,
        System.Text.Json.JsonElement input,
        ProductInvocationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(operation.Value)
            || !_registrations.TryGetValue(operation.Value, out var registration)
            || !_policyFilter.IsAvailable(context.AccessGrant, registration))
        {
            throw new ProductOperationNotAvailableException(operation);
        }

        if (string.IsNullOrWhiteSpace(context.IdempotencyKey.Value))
        {
            throw new ProductOperationInputException("An idempotency key is required.");
        }

        var validatedInput = registration.Json.ValidateAndCloneInput(input);
        var receipt = await registration.Adapter.InvokeAsync(
                operation,
                validatedInput,
                context,
                cancellationToken)
            .ConfigureAwait(false);
        if (receipt.Operation != operation)
        {
            throw new InvalidOperationException(
                "A product operation adapter returned a receipt for a different operation.");
        }

        return receipt;
    }

    private static void VerifyInstalledManifest(
        IModuleRegistry moduleRegistry,
        ProductOperationRegistration registration)
    {
        try
        {
            var manifest = moduleRegistry.Get(registration.DeclaredOperation.Owner);
            var declared = manifest.Operations
                .SingleOrDefault(candidate => candidate.Id == registration.DeclaredOperation.Id);
            if (declared is null || declared != registration.DeclaredOperation)
            {
                throw new ProductOperationCatalogConfigurationException(
                    $"Operation '{registration.DeclaredOperation.Id}' does not match its active module manifest.");
            }
        }
        catch (KeyNotFoundException exception)
        {
            throw new ProductOperationCatalogConfigurationException(
                $"Operation '{registration.DeclaredOperation.Id}' belongs to a module that is not installed.",
                exception);
        }
    }
}

public sealed class ProductOperationNotAvailableException : Exception
{
    public ProductOperationNotAvailableException(OperationId operation)
        : base($"Product operation '{operation}' is not available.")
    {
        Operation = operation;
    }

    public OperationId Operation { get; }
}
