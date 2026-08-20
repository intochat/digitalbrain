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

        builder.AddJournalStorage();
        builder.UseJsonJournalFormat(DurableStateJson.TypeInfoResolver);
        // Awaited publish: a subscriber's failure surfaces to the publisher, matching the
        // direct-call delivery semantics of Send.
        builder.AddBroadcastChannel(
            DigitalBrainNames.BroadcastChannelProvider,
            options => options.FireAndForgetDelivery = false);
        ModelPayloadSerialization.AddModelPayloadSerialization(builder.Services);

        foreach (var hook in ModuleHooksOf(modules))
        {
            hook.Configure(builder);
        }
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
