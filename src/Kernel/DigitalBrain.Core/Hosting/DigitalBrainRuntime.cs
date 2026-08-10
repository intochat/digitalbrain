using System.ComponentModel;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Journaling;
using Orleans.Journaling.Json;

namespace DigitalBrain.Core;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class DigitalBrainRuntime
{
    public static IReadOnlySet<ModuleId> Add(
        ISiloBuilder builder,
        IReadOnlyCollection<ICompiledModule> availableModules)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(availableModules);

        var selectedModules = SelectModules(builder, availableModules);
        var selected = availableModules
            .Where(module => selectedModules.Contains(module.Id))
            .OrderBy(module => module.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var capabilities = ActiveCapabilityCatalog.Create(selected);

        builder.AddJournalStorage();
        builder.UseJsonJournalFormat(JournalJsonContext.Default);
        builder.AddIncomingGrainCallFilter<IncomingReificationFilter>();
        builder.AddIncomingGrainCallFilter<OwnerBoundCallFilter>();
        builder.AddOutgoingGrainCallFilter<OutgoingReificationFilter>();
        builder.Services.AddSingleton(capabilities);
        builder.Services.AddSingleton(ActiveModuleContractTypeMap.Create(selected, capabilities));
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

        foreach (var module in availableModules)
        {
            module.PrepareSerialization(builder.Services);
        }

        foreach (var module in selected)
        {
            module.Activate(builder);
        }

        return selectedModules;
    }

    private static HashSet<ModuleId> SelectModules(ISiloBuilder builder, IReadOnlyCollection<ICompiledModule> availableModules)
    {
        var hostContext = builder.Services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(HostBuilderContext))
            ?.ImplementationInstance as HostBuilderContext
            ?? throw new InvalidOperationException(
                "DigitalBrain requires the .NET Generic Host so the AppHost module manifest can be validated.");
        var declared = hostContext.Configuration
            .GetSection("DigitalBrain:Modules")
            .GetChildren()
            .Select(section => new ModuleId(section.Value
                ?? throw new InvalidOperationException(
                    "DigitalBrain:Modules contains an empty module identity.")))
            .ToArray();

        var selectedModules = declared.ToHashSet();

        if (selectedModules.Count != declared.Length)
        {
            throw new InvalidOperationException(
                "DigitalBrain:Modules contains a duplicate module. Configure each module exactly once.");
        }

        var unavailableModules = selectedModules
            .Except(availableModules.Select(module => module.Id))
            .OrderBy(module => module.Value, StringComparer.Ordinal)
            .ToArray();

        if (unavailableModules.Length > 0)
        {
            throw new InvalidOperationException(
                "The AppHost selected module(s) absent from this silo's generated catalog: "
                + string.Join(", ", unavailableModules)
                + ". Add the corresponding runtime package reference to the silo.");
        }

        return selectedModules;
    }
}
