using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Journaling;
using System.Runtime.InteropServices;
using DigitalBrain.Runtime.Security;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Runtime.Filters;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Neurons.State;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Catalog;
using DigitalBrain.Runtime.Streams;

namespace DigitalBrain.Kernel;

public static class AddDigitalBrainSiloExtensions
{
    public static IHostApplicationBuilder AddDigitalBrainSilo(this IHostApplicationBuilder builder)
    {
        builder.UseOrleans(silo =>
        {
            var clusterId = builder.Configuration["ORLEANS_CLUSTER_ID"];
            var redisConn = builder.Configuration.GetConnectionString("orleans-redis")
                ?? builder.Configuration["ConnectionStrings:orleans-redis"];

            var isTesting = string.Equals(builder.Configuration["DigitalBrain__Mode"], "Testing", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(redisConn) || isTesting)
            {
                if (isTesting)
                {
                    var uniqueId = Guid.NewGuid().ToString();
                    var siloPort = GetFreePort();
                    var gatewayPort = GetFreePort();
                    silo.UseLocalhostClustering(
                        siloPort: siloPort,
                        gatewayPort: gatewayPort,
                        serviceId: uniqueId,
                        clusterId: uniqueId);
                }
                else if (string.IsNullOrEmpty(clusterId))
                {
                    silo.UseLocalhostClustering();
                }
                else
                {
                    silo.UseLocalhostClustering(
                        serviceId: clusterId,
                        clusterId: clusterId);
                }
                silo.AddMemoryGrainStorage("digitalbrain");
            }

            silo.Configure<Orleans.Configuration.EndpointOptions>(options =>
            {
                options.AdvertisedIPAddress = System.Net.IPAddress.Loopback;
            });

            silo.Services.TryAddSingleton<ISynapsePersistenceService, NoopSynapsePersistenceService>();
            silo.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
            silo.AddStateMachineStorage();

            ConfigureSynapseStreams(silo, StreamProviderConfig.ResolveMode(builder.Configuration));
            silo.AddActivityPropagation();

            silo.Services.AddSingleton<NeuronCatalogScanner>();
            silo.AddStartupTask<NeuronCatalogScanner>();

            silo.AddStartupTask((sp, ct) =>
            {
                var sdkRuntimeType = Type.GetType("DigitalBrain.SDK.SdkRuntime, DigitalBrain.SDK");
                if (sdkRuntimeType is not null)
                {
                    var spProp = sdkRuntimeType.GetProperty("ServiceProvider", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    var gfProp = sdkRuntimeType.GetProperty("GrainFactory", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                    spProp?.SetValue(null, sp);
                    gfProp?.SetValue(null, sp.GetRequiredService<IGrainFactory>());
                }
                return Task.CompletedTask;
            });

            silo.Services.AddSingleton(
                typeof(IAttributeToFactoryMapper<NeuronStateAttribute>),
                typeof(NeuronStateAttributeMapper));

            silo.Services.AddSingleton(
                typeof(IAttributeToFactoryMapper<NeuronSettingAttribute>),
                typeof(NeuronSettingAttributeMapper));

            // Filters register through the silo builder so Orleans owns the
            // IGrainContextAccessor wiring inside its own services scope.
            // Registering on the host root would surface a constructor-DI cycle
            // (filter -> IGrainContextAccessor -> HostedClient -> filters) that
            // the .NET DI validator rejects at startup.
            silo.AddOutgoingGrainCallFilter<CallerStampingOutgoingFilter>();
            silo.AddIncomingGrainCallFilter<QuerySynapseSynthesizingIncomingFilter>();
            silo.AddIncomingGrainCallFilter<NeuronContextFilter>();
        });

        builder.Services.AddSingleton<GrainRegistry>();
        builder.Services.AddTransient<Orleans.Metadata.IGrainTypeProvider, DynamicNeuronGrainTypeProvider>();

        // Get all already-registered IGrainContextActivatorProvider services to avoid circular dependency
        var existingActivatorDescriptors = builder.Services
            .Where(d => d.ServiceType == typeof(Orleans.Runtime.IGrainContextActivatorProvider))
            .ToList();

        int activatorIdx = 0;
        foreach (var desc in existingActivatorDescriptors)
        {
            builder.Services.Remove(desc);

            if (desc.ImplementationType != null)
            {
                builder.Services.AddKeyedSingleton(typeof(Orleans.Runtime.IGrainContextActivatorProvider), $"original-activator-{activatorIdx}", desc.ImplementationType);
            }
            else if (desc.ImplementationFactory != null)
            {
                builder.Services.AddKeyedSingleton(typeof(Orleans.Runtime.IGrainContextActivatorProvider), $"original-activator-{activatorIdx}", (sp, key) => desc.ImplementationFactory(sp));
            }
            else if (desc.ImplementationInstance != null)
            {
                builder.Services.AddKeyedSingleton(typeof(Orleans.Runtime.IGrainContextActivatorProvider), $"original-activator-{activatorIdx}", desc.ImplementationInstance);
            }
            activatorIdx++;
        }

        builder.Services.AddSingleton<Orleans.Runtime.IGrainContextActivatorProvider>(sp =>
        {
            var originals = new List<Orleans.Runtime.IGrainContextActivatorProvider>();
            for (int k = 0; k < activatorIdx; k++)
            {
                var original = sp.GetKeyedService<Orleans.Runtime.IGrainContextActivatorProvider>($"original-activator-{k}");
                if (original != null)
                {
                    originals.Add(original);
                }
            }
            return new DynamicNeuronContextActivatorProvider(sp, originals);
        });

        // Decorate IClusterManifestProvider to enrich the grain manifests dynamically
        var originalDescriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(Orleans.Runtime.IClusterManifestProvider));
        if (originalDescriptor != null)
        {
            builder.Services.Remove(originalDescriptor);

            if (originalDescriptor.ImplementationType != null)
            {
                builder.Services.AddSingleton(originalDescriptor.ImplementationType);
                 builder.Services.AddSingleton<Orleans.Runtime.IClusterManifestProvider>(sp =>
                {
                    var inner = (Orleans.Runtime.IClusterManifestProvider)sp.GetRequiredService(originalDescriptor.ImplementationType);
                    return new DynamicClusterManifestProvider(inner, sp);
                });
            }
            else if (originalDescriptor.ImplementationFactory != null)
            {
                builder.Services.AddSingleton<Orleans.Runtime.IClusterManifestProvider>(sp =>
                {
                    var inner = (Orleans.Runtime.IClusterManifestProvider)originalDescriptor.ImplementationFactory(sp);
                    return new DynamicClusterManifestProvider(inner, sp);
                });
            }
            else if (originalDescriptor.ImplementationInstance != null)
            {
                builder.Services.AddSingleton<Orleans.Runtime.IClusterManifestProvider>(sp =>
                {
                    var inner = (Orleans.Runtime.IClusterManifestProvider)originalDescriptor.ImplementationInstance;
                    return new DynamicClusterManifestProvider(inner, sp);
                });
            }
        }

        builder.Services.AddSingleton<INeuronTestRunner, NeuronTestRunner>();
        builder.Services.AddSingleton<INeuronStateProtector>(_ =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new DpapiNeuronStateProtector()
                : new InMemoryNeuronStateProtector());

        // Standard Microsoft DI Keyed Services dynamic mappings to eliminate custom routing tables
        // Standard Microsoft DI Keyed Services dynamic mappings to eliminate custom routing tables
        builder.Services.AddKeyedTransient<ICallNeuronTarget>(KeyedService.AnyKey, (sp, key) =>
        {
            var grainFactory = sp.GetRequiredService<IGrainFactory>();
            string targetFqn;
            string primaryKey;

            if (key != null && key.GetType().Name == "NeuronBinding")
            {
                var targetFqnProp = key.GetType().GetProperty("TargetFqn");
                var keyProp = key.GetType().GetProperty("Key");
                targetFqn = targetFqnProp?.GetValue(key) as string ?? "";
                primaryKey = (keyProp?.GetValue(key) as string) ?? targetFqn;
            }
            else
            {
                targetFqn = key?.ToString() ?? "";
                if (string.IsNullOrEmpty(targetFqn))
                    throw new InvalidOperationException("Key for ICallNeuronTarget keyed service cannot be empty.");
                primaryKey = targetFqn;
            }

            if (targetFqn == "DigitalBrain.Kernel.Settings.SettingsStore")
            {
                primaryKey = BrainScopeHelper.GetActiveScope();
            }
            else
            {
                primaryKey = BrainScopeHelper.GetActiveScopedNeuronKey(primaryKey);
            }
            return (ICallNeuronTarget)sp.GetRequiredService<GrainRegistry>().Resolve(targetFqn, primaryKey, typeof(ICallNeuronTarget));
        });

        builder.Services.AddKeyedTransient<IStreamNeuronTarget>(KeyedService.AnyKey, (sp, key) =>
        {
            var grainFactory = sp.GetRequiredService<IGrainFactory>();
            string targetFqn;
            string primaryKey;

            if (key != null && key.GetType().Name == "NeuronBinding")
            {
                var targetFqnProp = key.GetType().GetProperty("TargetFqn");
                var keyProp = key.GetType().GetProperty("Key");
                targetFqn = targetFqnProp?.GetValue(key) as string ?? "";
                primaryKey = (keyProp?.GetValue(key) as string) ?? targetFqn;
            }
            else
            {
                targetFqn = key?.ToString() ?? "";
                if (string.IsNullOrEmpty(targetFqn))
                    throw new InvalidOperationException("Key for IStreamNeuronTarget keyed service cannot be empty.");
                primaryKey = targetFqn;
            }

            primaryKey = BrainScopeHelper.GetActiveScopedNeuronKey(primaryKey);
            return (IStreamNeuronTarget)sp.GetRequiredService<GrainRegistry>().Resolve(targetFqn, primaryKey, typeof(IStreamNeuronTarget));
        });

        builder.Services.AddKeyedTransient<IResourceNeuronTarget>(KeyedService.AnyKey, (sp, key) =>
        {
            var grainFactory = sp.GetRequiredService<IGrainFactory>();
            string targetFqn;
            string primaryKey;

            if (key != null && key.GetType().Name == "NeuronBinding")
            {
                var targetFqnProp = key.GetType().GetProperty("TargetFqn");
                var keyProp = key.GetType().GetProperty("Key");
                targetFqn = targetFqnProp?.GetValue(key) as string ?? "";
                primaryKey = (keyProp?.GetValue(key) as string) ?? targetFqn;
            }
            else
            {
                targetFqn = key?.ToString() ?? "";
                if (string.IsNullOrEmpty(targetFqn))
                    throw new InvalidOperationException("Key for IResourceNeuronTarget keyed service cannot be empty.");
                primaryKey = targetFqn;
            }

            primaryKey = BrainScopeHelper.GetActiveScopedNeuronKey(primaryKey);
            return (IResourceNeuronTarget)sp.GetRequiredService<GrainRegistry>().Resolve(targetFqn, primaryKey, typeof(IResourceNeuronTarget));
        });

        builder.Services.AddKeyedTransient<IPredicateNeuronTarget>(KeyedService.AnyKey, (sp, key) =>
        {
            var grainFactory = sp.GetRequiredService<IGrainFactory>();
            string targetFqn;
            string primaryKey;

            if (key != null && key.GetType().Name == "NeuronBinding")
            {
                var targetFqnProp = key.GetType().GetProperty("TargetFqn");
                var keyProp = key.GetType().GetProperty("Key");
                targetFqn = targetFqnProp?.GetValue(key) as string ?? "";
                primaryKey = (keyProp?.GetValue(key) as string) ?? targetFqn;
            }
            else
            {
                targetFqn = key?.ToString() ?? "";
                if (string.IsNullOrEmpty(targetFqn))
                    throw new InvalidOperationException("Key for IPredicateNeuronTarget keyed service cannot be empty.");
                primaryKey = targetFqn;
            }

            primaryKey = BrainScopeHelper.GetActiveScopedNeuronKey(primaryKey);
            return (IPredicateNeuronTarget)sp.GetRequiredService<GrainRegistry>().Resolve(targetFqn, primaryKey, typeof(IPredicateNeuronTarget));
        });

        return builder;
    }

    static void ConfigureSynapseStreams(ISiloBuilder silo, StreamProviderMode mode)
    {
        switch (mode)
        {
            case StreamProviderMode.Memory:
                silo.AddMemoryStreams(StreamProviderConfig.SynapseProviderName)
                    .AddMemoryGrainStorage(StreamProviderConfig.PubSubStoreName);
                silo.AddMemoryStreams("synapse-streams")
                    .AddMemoryGrainStorage("PubSubStore");
                break;

            case StreamProviderMode.Redis:
                throw new NotImplementedException(
                    "Redis stream mode is reserved for the production-streams PR; not implemented yet.");
        }
    }

    private static int GetFreePort()
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        return ((System.Net.IPEndPoint)socket.LocalEndPoint!).Port;
    }
}

internal sealed class NoopSynapsePersistenceService : ISynapsePersistenceService
{
    public Task SaveSynapseAsync(Synapse synapse, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
