using Brain.Kernel;
using Brain.Modules.Flutter;
using Brain.Modules.Behaviors;
using Brain.Modules.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace Brain.KernelTests;

public sealed class BehaviorsKindsConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddBrainKernel(new ChatKind(), new WindowKind());
        siloBuilder.AddBrainKind("behavior", sp => new BehaviorKind(sp.GetRequiredService<IGrainFactory>()));
    }
}
