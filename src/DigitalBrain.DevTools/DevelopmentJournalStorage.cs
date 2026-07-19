using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.DevTools;

public static class DevelopmentJournalStorage
{
    public static ISiloBuilder AddDevelopmentJournalStorage(this ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());

        return builder;
    }
}
