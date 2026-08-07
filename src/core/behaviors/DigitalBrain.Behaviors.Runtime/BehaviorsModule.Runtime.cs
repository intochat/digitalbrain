using DigitalBrain.Security;
using DigitalBrain.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Behaviors.Runtime;

public sealed partial class BehaviorsModule
{
    static partial void ConfigureRuntime(ISiloBuilder builder)
    {
        DurablePayloadProtectionHosting.Configure(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(static provider =>
            new BehaviorCompiler(provider.GetRequiredService<DigitalBrain.Core.ActiveCapabilityCatalog>()));
        builder.Services.AddSingleton<IBehaviorBddGate, InstallTestsBddGate>();
        builder.Services.TryAddSingleton<IBehaviorArtifactTrust>(static provider =>
            new BehaviorArtifactTrust(provider.GetRequiredService<IDurablePayloadProtector>()));
        builder.Services.TryAddSingleton<IBehaviorProtectedPayloadAccess, GrainBehaviorProtectedPayloadAccess>();
        builder.Services.TryAddSingleton<IBehaviorProtectedTriggerAccess, GrainBehaviorProtectedTriggerAccess>();
        builder.Services.TryAddSingleton<IBehaviorTaskOperationAccess, GrainBehaviorTaskOperationAccess>();
        builder.Services.TryAddSingleton<IBehaviorCapabilityDispatchAccess, GrainBehaviorCapabilityDispatchAccess>();
        builder.Services.TryAddSingleton<DigitalBrain.Core.IBroadcastSubscribers, BehaviorBroadcastSubscribers>();
        builder.Services.TryAddSingleton<IUserActionCustody>(static provider =>
        {
            var time = provider.GetKeyedService<TimeProvider>(DigitalBrain.Core.NeuronTime.ServiceKey)
                ?? provider.GetService<TimeProvider>()
                ?? TimeProvider.System;
            return new GrainUserActionCustody(
                provider.GetRequiredService<IBehaviorProtectedPayloadAccess>(),
                time);
        });

        // No external Host worker: authored assemblies never load in the silo process.
        builder.Services.AddSingleton<IBehaviorExecutor, InProcessBehaviorExecutor>();
    }
}
