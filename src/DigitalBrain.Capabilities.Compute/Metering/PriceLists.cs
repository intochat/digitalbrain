using DigitalBrain.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Capabilities.Compute;

public sealed class ModulePriceListRegistration(string moduleId, Type priceListType, ExternalOperationDescriptor[] operations)
{
    private readonly ExternalOperationDescriptor[] _operations = [.. operations];
    public string ModuleId { get; } = moduleId;
    public Type PriceListType { get; } = priceListType;
    public ExternalOperationDescriptor[] Operations => [.. _operations];
}

public sealed class ModulePriceListRegistry
{
    private readonly Dictionary<string, (ModulePriceListRegistration Owner, ExternalOperationDescriptor Operation)> _operations = new(StringComparer.Ordinal);

    public ModulePriceListRegistry(IEnumerable<ModulePriceListRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        foreach (var registration in registrations)
        {
            foreach (var operation in registration.Operations)
            {
                if (!_operations.TryAdd(operation.Name, (registration, operation)))
                { throw new InvalidOperationException($"External operation '{operation.Name}' has multiple owners."); }
            }
        }
    }

    public ModulePriceListRegistration Resolve(string operation) => Find(operation).Owner;
    public ExternalOperationDescriptor GetOperation(string operation) => Find(operation).Operation;

    private (ModulePriceListRegistration Owner, ExternalOperationDescriptor Operation) Find(string operation)
        => _operations.TryGetValue(operation, out var declaration)
            ? declaration
            : throw new InvalidOperationException($"External operation '{operation}' is not declared by a module.");
}

public static class ModulePriceLists
{
    public static IServiceCollection AddModulePriceList<TPriceList>(this IServiceCollection services,
        string moduleId, ExternalOperationDescriptor[] operations)
        where TPriceList : class, IPriceList
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(operations);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ValidateToken(operation.Name, nameof(operations));
            ValidateToken(operation.Integration, nameof(operations));
            ValidateToken(operation.Unit, nameof(operations));
            if (!names.Add(operation.Name))
            { throw new InvalidOperationException($"External operation '{operation.Name}' is declared more than once."); }
        }
        var declared = operations.OrderBy(operation => operation.Name, StringComparer.Ordinal).ToArray();

        var registrations = services.Where(descriptor => descriptor.ServiceType == typeof(ModulePriceListRegistration))
            .Select(descriptor => descriptor.ImplementationInstance as ModulePriceListRegistration
                ?? throw new InvalidOperationException("Module price lists must be registered with AddModulePriceList."))
            .ToArray();
        foreach (var registration in registrations)
        {
            if (registration.ModuleId == moduleId)
            {
                if (registration.PriceListType == typeof(TPriceList) && registration.Operations.SequenceEqual(declared))
                { return services; }
                throw new InvalidOperationException($"Module '{moduleId}' has conflicting price-list declarations.");
            }
            if (registration.Operations.Any(operation => names.Contains(operation.Name)))
            { throw new InvalidOperationException("External operation ownership overlaps between modules."); }
        }

        if (services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(TPriceList)
            && descriptor.Lifetime != ServiceLifetime.Singleton))
        { throw new InvalidOperationException($"Module price list '{typeof(TPriceList).Name}' must be registered as a singleton."); }

        if (!registrations.Any(registration => registration.PriceListType == typeof(TPriceList))
            && services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(IPriceList)
                && (descriptor.ImplementationType == typeof(TPriceList)
                    || descriptor.ImplementationInstance is TPriceList
                    || descriptor.ImplementationFactory?.GetType().GenericTypeArguments.LastOrDefault() == typeof(TPriceList))))
        { throw new InvalidOperationException($"Register '{typeof(TPriceList).Name}' through AddModulePriceList so concrete and IPriceList resolution share one singleton."); }

        services.TryAddSingleton<TPriceList>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPriceList, TPriceList>(sp => sp.GetRequiredService<TPriceList>()));
        services.AddSingleton(new ModulePriceListRegistration(moduleId, typeof(TPriceList), declared));
        services.TryAddSingleton<ModulePriceListRegistry>();
        return services;
    }

    private static void ValidateToken(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-' and not '/'))
        { throw new ArgumentException("Operation names, integrations and units must be canonical tokens of at most 128 characters.", parameter); }
    }
}
