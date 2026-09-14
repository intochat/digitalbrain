using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Slots;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Journaling;
using Orleans.Journaling.Json;

namespace DigitalBrain.Core;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class DigitalBrainRuntime
{
    public static void Add(ISiloBuilder builder, ModuleManifest modules)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(modules);

        builder.AddStartupTask(static (services, _) =>
        {
            if (services.GetService<Orleans.IReminderTable>() is null)
            {
                throw new InvalidOperationException(
                    "Neurons hold a retry reminder for pending work. Configure UseAzureTableReminderService for a real host or UseInMemoryReminderService for a test host.");
            }

            // A slot that resolved the single-slot lease would react to shared reminders and shared
            // pending work while another slot serves traffic (design section 8, finding 1).
            if (services.GetRequiredService<IConfiguration>()[ActiveSlotNames.SlotKey] is { Length: > 0 } slot
                && services.GetRequiredService<IActiveSlotLease>() is SingleSlotLease)
            {
                throw new InvalidOperationException(
                    $"Slot '{slot}' has no active-slot lease store, so nothing would fence it. Register one (AddDigitalBrain does when the '{DigitalBrainNames.Clustering}' connection string is set) or clear '{ActiveSlotNames.SlotKey}'.");
            }

            // Building the table here turns a grain class descriptor violation into a silo-start failure.
            services.GetRequiredService<DescriptorTable>();
            return Task.CompletedTask;
        });

        builder.AddJournalStorage();
        builder.AddIncomingGrainCallFilter<NeuronActivationGuardFilter>();
        builder.AddOutgoingGrainCallFilter<CommandLocalityFilter>();
        builder.AddOutgoingGrainCallFilter<OutgoingCallerFilter>();
        builder.AddActivityPropagation();
        builder.UseJsonJournalFormat(DurableStateJson.TypeInfoResolver);
        ModelPayloadSerialization.AddModelPayloadSerialization(builder.Services);
        builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.TryAddSingleton<IActiveSlotLease>(static services =>
            new SingleSlotLease(services.GetRequiredService<IConfiguration>()[ActiveSlotNames.SlotKey] ?? string.Empty));
        builder.Services.TryAddSingleton<NeuronOptions>();
        builder.Services.TryAddSingleton<NeuronRuntime>();
        builder.Services.TryAddSingleton<StreamWake>();
        builder.Services.TryAddSingleton<DescriptorTable>();
        builder.Services.TryAddSingleton<INeuronInvoker, NeuronInvoker>();

        foreach (var hook in ModuleHooksOf(modules))
        {
            hook.Configure(builder);
        }
    }

    private static IEnumerable<IModule> ModuleHooksOf(ModuleManifest modules)
        => modules.Types.Select(static type =>
        {
            if (type is not { IsClass: true, IsAbstract: false }
                || !typeof(IModule).IsAssignableFrom(type)
                || type.GetConstructor(Type.EmptyTypes) is null)
            {
                throw new InvalidOperationException(
                    $"Configured module '{type.FullName}' must be a concrete {nameof(IModule)} with a public parameterless constructor.");
            }

            return (IModule)Activator.CreateInstance(type)!;
        });
}
