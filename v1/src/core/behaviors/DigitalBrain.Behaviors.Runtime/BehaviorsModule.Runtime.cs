using DigitalBrain.Security;
using DigitalBrain.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Behaviors.Runtime;

public sealed partial class BehaviorsModule
{
    public const string ExecutorConfigurationKey = "DigitalBrain:Behaviors:Executor";
    public const string HostBaseAddressConfigurationKey = "DigitalBrain:Behaviors:Host:BaseAddress";
    public const string HostExecutorName = "Host";
    public const string InProcessExecutorName = "InProcess";

    static partial void ConfigureRuntime(ISiloBuilder builder)
    {
        DurablePayloadProtectionHosting.Configure(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(static provider =>
            new BehaviorCompiler(provider.GetRequiredService<DigitalBrain.Kernel.ActiveCapabilityCatalog>()));
        builder.Services.AddSingleton<IBehaviorBddGate, InstallTestsBddGate>();
        builder.Services.TryAddSingleton<IBehaviorArtifactTrust>(static provider =>
            new BehaviorArtifactTrust(provider.GetRequiredService<IDurablePayloadProtector>()));
        builder.Services.TryAddSingleton<IBehaviorProtectedPayloadAccess, GrainBehaviorProtectedPayloadAccess>();
        builder.Services.TryAddSingleton<IBehaviorProtectedTriggerAccess, GrainBehaviorProtectedTriggerAccess>();
        builder.Services.TryAddSingleton<IBehaviorTaskOperationAccess, GrainBehaviorTaskOperationAccess>();
        builder.Services.TryAddSingleton<IBehaviorCapabilityDispatchAccess, GrainBehaviorCapabilityDispatchAccess>();
        builder.Services.TryAddSingleton<DigitalBrain.Kernel.IBroadcastSubscribers, BehaviorBroadcastSubscribers>();
        builder.Services.TryAddSingleton<IUserActionCustody>(static provider =>
        {
            var time = provider.GetKeyedService<TimeProvider>(DigitalBrain.Kernel.NeuronTime.ServiceKey)
                ?? provider.GetService<TimeProvider>()
                ?? TimeProvider.System;
            return new GrainUserActionCustody(
                provider.GetRequiredService<IBehaviorProtectedPayloadAccess>(),
                time);
        });

        var executor = builder.Configuration[ExecutorConfigurationKey];
        var baseAddress = builder.Configuration[HostBaseAddressConfigurationKey];
        var useHost =
            string.Equals(executor, HostExecutorName, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(baseAddress)
                && !string.Equals(executor, InProcessExecutorName, StringComparison.OrdinalIgnoreCase));

        if (useHost)
        {
            if (!string.IsNullOrWhiteSpace(baseAddress))
            {
                builder.Services.AddHttpClient<IBehaviorHostGateway, HttpBehaviorHostClient>(client =>
                {
                    client.BaseAddress = new Uri(baseAddress, UriKind.Absolute);
                });
            }

            builder.Services.AddSingleton<IBehaviorExecutor, HostedBehaviorExecutor>();
            return;
        }

        // Closed residual only: never loads authored assemblies in the silo process.
        builder.Services.AddSingleton<IBehaviorExecutor, InProcessBehaviorExecutor>();
    }
}
