using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;

namespace DigitalBrain;

public static class DevelopmentJournalStorage
{
    public static ISiloBuilder AddDevelopmentJournalStorage(this ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());

        return builder;
    }
}
