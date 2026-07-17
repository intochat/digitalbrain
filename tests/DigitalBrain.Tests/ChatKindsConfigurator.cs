using Brain.Kernel;
using Brain.Modules.Workspace;
using Orleans.TestingHost;

namespace Brain.KernelTests;

public sealed class ChatKindsConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder) =>
        siloBuilder.AddBrainKernel(new ChatKind());
}
