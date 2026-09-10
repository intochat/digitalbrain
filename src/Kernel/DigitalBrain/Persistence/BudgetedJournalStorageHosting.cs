using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Core;

public static class BudgetedJournalStorageHosting
{
    public static ISiloBuilder UseBudgetedJournalStorage(this ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.UseBudgetedJournalStorage();
        return builder;
    }

    internal static IServiceCollection UseBudgetedJournalStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var descriptor = services.LastOrDefault(service => service.ServiceType == typeof(IJournalStorageProvider))
            ?? throw new InvalidOperationException(
                "Missing IJournalStorageProvider registration. Register journal storage before calling UseBudgetedJournalStorage.");
        services[services.IndexOf(descriptor)] = new ServiceDescriptor(typeof(IJournalStorageProvider), provider =>
        {
            var inner = (IJournalStorageProvider)(descriptor.ImplementationInstance
                ?? descriptor.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
            return new BudgetedJournalStorageProvider(inner, provider.GetRequiredService<NeuronOptions>().StorageOperationBudget);
        }, descriptor.Lifetime);
        return services;
    }
}
