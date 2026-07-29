using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Behaviors;

public sealed partial class BehaviorsModule
{
    static partial void ConfigureRuntime(ISiloBuilder builder)
    {
        builder.Services.AddSingleton<IBehaviorCompiler, ContractOnlyBehaviorCompiler>();
        builder.Services.AddSingleton<IBehaviorBddGate, InstallTestsBddGate>();
        builder.Services.AddSingleton<IBehaviorExecutor, InProcessBehaviorExecutor>();
    }
}
