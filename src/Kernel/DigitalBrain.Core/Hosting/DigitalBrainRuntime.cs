using System.ComponentModel;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Journaling.Json;

namespace DigitalBrain.Core;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class DigitalBrainRuntime
{
    public static void Add(ISiloBuilder builder, ModuleAssemblies modules)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(modules);

        var capabilities = ActiveCapabilityCatalog.Create(ManifestsOf(modules));

        builder.AddJournalStorage();
        builder.UseJsonJournalFormat(JournalJsonContext.Default);
        builder.AddIncomingGrainCallFilter<IncomingReificationFilter>();
        builder.AddIncomingGrainCallFilter<OwnerBoundCallFilter>();
        builder.AddOutgoingGrainCallFilter<OutgoingReificationFilter>();
        builder.Services.AddSingleton(capabilities);
        builder.Services.AddSingleton(
            ActiveModuleContractTypeMap.Create(
                modules.Contracts.Concat(modules.Implementations),
                capabilities));
        builder.Services.AddSingleton(services =>
        {
            var catalog = new BroadcastCatalog();

            foreach (var configure in services.GetServices<IConfigureBroadcastCatalog>())
            {
                configure.Configure(catalog);
            }

            return catalog;
        });
        builder.Services.AddSingleton(services =>
            new BroadcastTopology(services.GetRequiredService<BroadcastCatalog>().Routes()));

        ModelPayloadSerialization.AddModelPayloadSerialization(builder.Services);

        foreach (var implementation in modules.Implementations)
        {
            builder.Services.AddSingleton<IConfigureBroadcastCatalog>(
                new AssemblyBroadcastHandlers(implementation));
        }

        foreach (var hook in ModuleHooksOf(modules))
        {
            hook.Configure(builder);
        }
    }

    public static IReadOnlyList<CapabilityManifest> ManifestsOf(ModuleAssemblies modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        return
        [
            .. modules.Contracts
                .Select(ModuleReflection.ManifestOf)
                .OrderBy(static manifest => manifest.ModuleId.Value, StringComparer.Ordinal),
        ];
    }

    private static IEnumerable<IModule> ModuleHooksOf(ModuleAssemblies modules)
        => modules.Implementations
            .SelectMany(static assembly => assembly.GetTypes())
            .Where(static type => type is { IsClass: true, IsAbstract: false }
                && typeof(IModule).IsAssignableFrom(type)
                && type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(static type => (IModule)Activator.CreateInstance(type)!);
}
