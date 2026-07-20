using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.Runtime.MembershipService.SiloMetadata;

namespace DigitalBrain.Kernel;

public static class DigitalBrainSiloBuilderExtensions
{
    public static ISiloBuilder AddDigitalBrain(this ISiloBuilder builder) => builder.AddDigitalBrain(siloLabel: null);

    public static ISiloBuilder AddDigitalBrain(this ISiloBuilder builder, string? siloLabel)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(siloLabel))
        {
            metadata[PinToSiloDirector.SiloLabelKey] = siloLabel;
        }

        builder.AddJournalStorage();
        builder.UseJsonJournalFormat(JournalJsonContext.Default);
        builder.AddIncomingGrainCallFilter<OwnerBoundCallFilter>();
        builder.UseSiloMetadata(metadata);
        builder.Services.AddPinToSiloPlacement();
        builder.Services.AddSingleton(services =>
        {
            var catalog = new BroadcastCatalog();

            foreach (var configure in services.GetServices<IConfigureBroadcastCatalog>())
            {
                configure.Configure(catalog);
            }

            return catalog;
        });

        return builder;
    }

    public static ISiloBuilder AddBroadcastHandlers(this ISiloBuilder builder, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(assembly);

        builder.Services.AddSingleton<IConfigureBroadcastCatalog>(new AssemblyBroadcastHandlers(assembly));

        return builder;
    }
}

internal interface IConfigureBroadcastCatalog
{
    void Configure(BroadcastCatalog catalog);
}

internal sealed class AssemblyBroadcastHandlers(Assembly assembly) : IConfigureBroadcastCatalog
{
    public void Configure(BroadcastCatalog catalog) => catalog.AddAssembly(assembly);
}

