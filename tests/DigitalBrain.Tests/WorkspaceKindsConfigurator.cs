using Brain.Kernel;
using Brain.Modules.Flutter;
using Orleans.TestingHost;

namespace Brain.KernelTests;

public sealed class WorkspaceKindsConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddBrainKernel(new TestKind());
        siloBuilder.AddDigitalBrainFlutter();
    }
}
