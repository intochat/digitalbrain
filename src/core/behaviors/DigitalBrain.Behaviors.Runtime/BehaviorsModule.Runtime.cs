using DigitalBrain.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Behaviors;

public sealed partial class BehaviorsModule
{
    public const string ExecutorConfigurationKey = "DigitalBrain:Behaviors:Executor";
    public const string HostBaseAddressConfigurationKey = "DigitalBrain:Behaviors:Host:BaseAddress";
    public const string HostExecutorName = "Host";
    public const string InProcessExecutorName = "InProcess";

    static partial void ConfigureRuntime(ISiloBuilder builder)
    {
        DurablePayloadProtectionHosting.Configure(builder.Services, builder.Configuration);
        builder.Services.AddSingleton<IBehaviorCompiler>(static provider =>
            new ContractOnlyBehaviorCompiler(provider.GetRequiredService<DigitalBrain.Kernel.ActiveCapabilityCatalog>()));
        builder.Services.AddSingleton<IBehaviorBddGate, InstallTestsBddGate>();
        builder.Services.TryAddSingleton<IBehaviorArtifactTrust>(static provider =>
            new SiloBehaviorArtifactTrust(provider.GetRequiredService<IDurablePayloadProtector>()));
        builder.Services.TryAddSingleton<IBehaviorProtectedPayloadAccess, GrainBehaviorProtectedPayloadAccess>();
        builder.Services.TryAddSingleton<IBehaviorTaskOperationAccess, GrainBehaviorTaskOperationAccess>();
        builder.Services.TryAddSingleton<IBehaviorCapabilityDispatchAccess, GrainBehaviorCapabilityDispatchAccess>();

        var executor = builder.Configuration[ExecutorConfigurationKey];
        if (string.Equals(executor, HostExecutorName, StringComparison.OrdinalIgnoreCase))
        {
            var baseAddress = builder.Configuration[HostBaseAddressConfigurationKey];
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

        builder.Services.AddSingleton<IBehaviorExecutor, InProcessBehaviorExecutor>();
    }
}
