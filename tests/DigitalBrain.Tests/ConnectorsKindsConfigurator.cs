using Brain.Kernel;
using Brain.Kernel.Connections;
using Brain.Modules.Google;
using DigitalBrain.Tests;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace Brain.KernelTests;

public sealed class ConnectorsKindsConfigurator : ISiloConfigurator
{
    public static FakeConnectionProvider GoogleConnectionProvider { get; } = new();
    public static FakeGmailProvider GmailProvider { get; } = new();

    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddBrainKernel();
        siloBuilder.Services.AddKeyedSingleton<IConnectionProvider>("google", GoogleConnectionProvider);
        siloBuilder.Services.AddKeyedSingleton<IGmailProvider>("google", GmailProvider);
        siloBuilder.AddBrainKind("gmail", sp => new GmailKind(sp.GetRequiredService<IGrainFactory>(), sp));
    }
}
