using System.ComponentModel;
using DigitalBrain.Abstractions.Descriptors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization.Metadata;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.Serialization;

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
                    "Neurons hold a retry reminder for pending work. Configure a reminder service, such as UseAzureTableReminderService.");
            }
            return Task.CompletedTask;
        });

        builder.AddJournalStorage();
        builder.AddIncomingGrainCallFilter<NeuronActivationGuardFilter>();
        builder.AddOutgoingGrainCallFilter<OutgoingCallerFilter>();
        builder.AddActivityPropagation();
        builder.UseJsonJournalFormat(new DefaultJsonTypeInfoResolver());
        AddModelPayloadSerialization(builder.Services);
        builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddOptions<NeuronOptions>()
            .BindConfiguration(NeuronOptions.SectionName)
            .Validate(options => options.StorageOperationBudget > TimeSpan.Zero && options.RetryReminderPeriod > TimeSpan.Zero,
                "Neuron storage budget and retry reminder period must be positive.")
            .ValidateOnStart();
        builder.Services.TryAddSingleton(services => services.GetRequiredService<IOptions<NeuronOptions>>().Value);
        builder.Services.TryAddSingleton<NeuronRuntime>();
        AddInvoker(builder.Services);

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

    public static void AddClient(IClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AddInvoker(builder.Services);
    }

    public static void AddModelPayloadSerialization(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSerializer(serializer => serializer.AddJsonSerializer(static type => type == typeof(System.Text.Json.JsonElement)));
    }

    private static void AddInvoker(IServiceCollection services)
    {
        services.TryAddSingleton<NeuronToolCatalog>();
        services.TryAddSingleton<INeuronInvoker>(static services => new NeuronInvoker(
            services.GetRequiredService<IGrainFactory>(), () => services.GetRequiredService<NeuronToolCatalog>()));
    }
}
