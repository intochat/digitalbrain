using Brain.Kernel;
using Orleans.TestingHost;

namespace Brain.KernelTests;

public sealed class KernelKindsConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder) =>
        siloBuilder.AddBrainKernel(new TestKind(), new ProposerKind());
}
