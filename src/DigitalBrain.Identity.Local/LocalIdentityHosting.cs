using DigitalBrain.Identity.Local.Custody;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Identity.Local;

/// <summary>Registers the local identity, secrets vault, connections and file master-key custody on a host.</summary>
public static class LocalIdentityHosting
{
    /// <summary>
    /// Registers <see cref="FileMasterKeyCustody"/> under <paramref name="homeDirectory"/> and the local identity,
    /// vault and connections services resolved via <paramref name="neuronResolver"/>.
    /// </summary>
    public static IServiceCollection AddLocalIdentity(
        this IServiceCollection services,
        Func<Type, string, object> neuronResolver,
        string homeDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(neuronResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);
        services.TryAddSingleton<IMasterKeyCustody>(_ => new FileMasterKeyCustody(homeDirectory));
        return services.AddLocalIdentity(neuronResolver);
    }

    /// <summary>
    /// Registers the local identity, vault and connections services resolved via <paramref name="neuronResolver"/>
    /// over an already-registered <see cref="IMasterKeyCustody"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No <see cref="IMasterKeyCustody"/> is registered in <paramref name="services"/>.</exception>
    public static IServiceCollection AddLocalIdentity(
        this IServiceCollection services,
        Func<Type, string, object> neuronResolver)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(neuronResolver);
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IMasterKeyCustody)))
        {
            throw new InvalidOperationException(
                $"{nameof(AddLocalIdentity)} requires an {nameof(IMasterKeyCustody)} registration; pass a homeDirectory for {nameof(FileMasterKeyCustody)} or register {nameof(IMasterKeyCustody)} first.");
        }
        return services;
    }
}
