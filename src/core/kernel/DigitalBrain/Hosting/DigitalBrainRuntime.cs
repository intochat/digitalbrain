using System.ComponentModel;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.Runtime.MembershipService.SiloMetadata;

namespace DigitalBrain.Kernel;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class DigitalBrainRuntime
{
    public static IReadOnlySet<ModuleId> Add(
        ISiloBuilder builder,
        string? siloLabel,
        IReadOnlyCollection<ICompiledModule> availableModules)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(availableModules);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        var selectedModules = SelectModules(builder, availableModules);

        if (!string.IsNullOrWhiteSpace(siloLabel))
        {
            metadata[PinToSiloDirector.SiloLabelKey] = siloLabel;
        }

        builder.AddJournalStorage();
        builder.UseJsonJournalFormat(JournalJsonContext.Default);
        builder.AddIncomingGrainCallFilter<IncomingReificationFilter>();
        builder.AddIncomingGrainCallFilter<OwnerBoundCallFilter>();
        builder.AddOutgoingGrainCallFilter<OutgoingReificationFilter>();
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

        foreach (var module in availableModules)
        {
            module.PrepareSerialization(builder.Services);
        }

        foreach (var module in availableModules.Where(module => selectedModules.Contains(module.Id)))
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
