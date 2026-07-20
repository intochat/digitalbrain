using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Kernel;

public static class ModuleComposition
{
    public static ISiloBuilder AddModule<TModule>(this ISiloBuilder builder)
        where TModule : class, IModule, new()
    {
        ArgumentNullException.ThrowIfNull(builder);

        var module = new TModule();
        Validate(module);
        ModuleWiring.EnsureManifestMatchesReflection(typeof(TModule).Assembly);
        builder.Services.AddSingleton<IModule>(module);
        builder.AddBroadcastHandlers(typeof(TModule).Assembly);

        return builder;
    }

    public static void Validate(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var descriptor = module.Descriptor
            ?? throw new ModuleCompositionException(
                $"Module '{module.GetType().Name}' returned a null descriptor.");

        if (string.IsNullOrWhiteSpace(descriptor.Id))
        {
            throw new ModuleCompositionException(
                $"Module '{module.GetType().Name}' declares an empty id.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.Version))
        {
            throw new ModuleCompositionException(
                $"Module '{descriptor.Id}' declares an empty version.");
        }

        var duplicateCapability = descriptor.Capabilities
            .GroupBy(capability => capability.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateCapability is not null)
        {
            throw new ModuleCompositionException(
                $"Module '{module.GetType().Name}' declares duplicate capability '{duplicateCapability.Key}'.");
        }

        var duplicateSecret = descriptor.Secrets
            .GroupBy(secret => secret.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateSecret is not null)
        {
            throw new ModuleCompositionException(
                $"Module '{module.GetType().Name}' declares duplicate secret '{duplicateSecret.Key}'.");
        }

        var duplicateConnection = descriptor.Connections
            .GroupBy(connection => connection.Provider, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateConnection is not null)
        {
            throw new ModuleCompositionException(
                $"Module '{module.GetType().Name}' declares duplicate connection provider '{duplicateConnection.Key}'.");
        }
    }
}
