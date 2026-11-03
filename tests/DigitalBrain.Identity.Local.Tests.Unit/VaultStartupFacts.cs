using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Identity.Local.Tests.Unit;

public sealed class VaultStartupFacts
{
    [Fact]
    public void VaultWithoutCustodyFailsToStart()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddLocalIdentity((_, _) => new object()));
    }
}

internal sealed class ServiceCollection : List<ServiceDescriptor>, IServiceCollection
{
    public SimpleServiceProvider BuildServiceProvider() => new(this);
}

internal sealed class SimpleServiceProvider(IReadOnlyList<ServiceDescriptor> descriptors) : IServiceProvider
{
    private readonly Dictionary<Type, object?> _singletons = new();

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider))
        {
            return this;
        }

        if (_singletons.TryGetValue(serviceType, out var cached))
        {
            return cached;
        }

        var descriptor = descriptors.LastOrDefault(d => !d.IsKeyedService && d.ServiceType == serviceType);
        if (descriptor is null)
        {
            return null;
        }

        var instance = descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(this)
            ?? Activator.CreateInstance(descriptor.ImplementationType!);
        if (descriptor.Lifetime == ServiceLifetime.Singleton)
        {
            _singletons[serviceType] = instance;
        }

        return instance;
    }
}
