using Brain.Kernel;
using Brain.Modules.Connections;
using Brain.Modules.Google;
using Brain.Modules.Salesforce;
using Brain.Modules.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace Brain.KernelTests;

public sealed class ConnectorsKindsConfigurator : ISiloConfigurator
{
    public static FakeConnectionProvider GoogleConnectionProvider { get; } = new();
    public static FakeConnectionProvider SalesforceConnectionProvider { get; } = new();
    public static FakeGmailProvider GmailProvider { get; } = new();
    public static FakeSalesforceProvider SalesforceProvider { get; } = new();

    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddBrainKernel();
        siloBuilder.Services.AddKeyedSingleton<IConnectionProvider>("google", GoogleConnectionProvider);
        siloBuilder.Services.AddKeyedSingleton<IConnectionProvider>("salesforce", SalesforceConnectionProvider);
        siloBuilder.Services.AddKeyedSingleton<IGmailProvider>("google", GmailProvider);
        siloBuilder.Services.AddKeyedSingleton<ISalesforceProvider>("salesforce", SalesforceProvider);
        siloBuilder.AddBrainKind("connection", sp => new ConnectionKind(sp));
        siloBuilder.AddBrainKind("gmail", sp => new GmailKind(sp.GetRequiredService<IGrainFactory>(), sp));
        siloBuilder.AddBrainKind("salesforce", sp => new SalesforceKind(sp.GetRequiredService<IGrainFactory>(), sp));
    }
}
