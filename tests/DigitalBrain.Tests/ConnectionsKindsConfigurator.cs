using Brain.Kernel;
using Brain.Kernel.Connections;
using DigitalBrain.Tests;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace Brain.KernelTests;

public sealed class ConnectionsKindsConfigurator : ISiloConfigurator
{
    public static FakeConnectionProvider GoogleProvider { get; } = new();
    public static FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddBrainKernel();
        siloBuilder.Services.AddKeyedSingleton<IConnectionProvider>("google", GoogleProvider);
        siloBuilder.Services.AddSingleton<TimeProvider>(Clock);
    }
}
