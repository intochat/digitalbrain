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
    private readonly IModuleRegistry _moduleRegistry;
    private readonly long _moduleGeneration;
    private readonly ProductOperationPolicyFilter _policyFilter;

    public ProductOperationCatalog(
        IModuleRegistry moduleRegistry,
        ProductOperationPolicyFilter policyFilter,
        IReadOnlyCollection<ProductOperationRegistration> registrations)
    {
        _moduleRegistry = moduleRegistry ?? throw new ArgumentNullException(nameof(moduleRegistry));
        _policyFilter = policyFilter ?? throw new ArgumentNullException(nameof(policyFilter));
        ArgumentNullException.ThrowIfNull(registrations);

        using var snapshotLease = moduleRegistry.AcquireSnapshot();
        var activeSnapshot = snapshotLease.Snapshot;
        _moduleGeneration = activeSnapshot.Generation;
        var index = new SortedDictionary<string, ProductOperationRegistration>(StringComparer.Ordinal);
        var northboundIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            VerifyInstalledManifest(activeSnapshot.Modules, registration);
            if (!index.TryAdd(registration.DeclaredOperation.Id.Value, registration))
            {
                throw new ProductOperationCatalogConfigurationException(
                    $"Operation '{registration.DeclaredOperation.Id}' has more than one product registration.");
            }

            if (!northboundIdentities.Add(registration.Identity.NorthboundIdentity))
            {
                throw new ProductOperationCatalogConfigurationException(
                    $"Operation '{registration.DeclaredOperation.Id}' collides with another northbound product identity.");
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

        using var snapshotLease = _moduleRegistry.AcquireSnapshot();
        if (!IsActiveGeneration(snapshotLease.Snapshot))
        {
            return Task.FromResult<IReadOnlyList<ProductOperationDescriptor>>([]);
        }

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

        Task<ProductActivityReceipt> invocation;
        using (var snapshotLease = _moduleRegistry.AcquireSnapshot())
        {
            if (!IsActiveGeneration(snapshotLease.Snapshot)
                || string.IsNullOrWhiteSpace(operation.Value)
                || !_registrations.TryGetValue(operation.Value, out var registration)
                || !_policyFilter.IsAvailable(context.AccessGrant, registration))
            {
                throw new ProductOperationNotAvailableException(operation);
            }

            if (string.IsNullOrWhiteSpace(context.IdempotencyKey.Value))
            {
                throw new ProductOperationInputException("An idempotency key is required.");
            }

            invocation = registration.Adapter.InvokeAsync(
                operation,
                input,
                context,
                cancellationToken);
        }

        var receipt = await invocation.ConfigureAwait(false);
        if (receipt.Operation != operation)
        {
            throw new InvalidOperationException(
                "A product operation adapter returned a receipt for a different operation.");
        }

        return receipt;
    }

    private static void VerifyInstalledManifest(
        ModuleSet activeModules,
        ProductOperationRegistration registration)
    {
        var manifest = activeModules.Modules.SingleOrDefault(
            module => module.Id == registration.DeclaredOperation.Owner);
        if (manifest is null)
        {
            throw new ProductOperationCatalogConfigurationException(
                $"Operation '{registration.DeclaredOperation.Id}' belongs to a module that is not installed.");
        }

        var declared = manifest.Operations
            .SingleOrDefault(candidate => candidate.Id == registration.DeclaredOperation.Id);
        if (declared is null || declared != registration.DeclaredOperation)
        {
            throw new ProductOperationCatalogConfigurationException(
                $"Operation '{registration.DeclaredOperation.Id}' does not match its active module manifest.");
        }
    }

    private bool IsActiveGeneration(ModuleRegistrySnapshot snapshot)
        => snapshot.Generation == _moduleGeneration;
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
