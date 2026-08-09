using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Harness;

public sealed class BrainClusterFixture : IAsyncLifetime
{
    private const int SiloCount = 2;
    private static readonly TimeSpan SaturatedMachineResponseTimeout = TimeSpan.FromSeconds(90);

    private readonly IJournalStorageProvider _sharedJournalStorage = new VolatileJournalStorageProvider();
    private InProcessTestCluster? _cluster;

    public IDigitalBrain BrainFor(string owner)
        => DigitalBrainClient.Connect(Cluster.Client, owner);

    private InProcessTestCluster Cluster
        => _cluster ?? throw new InvalidOperationException("The brain cluster is not running.");

    public async ValueTask InitializeAsync()
    {
        ICompiledModule[] modules =
        [
            new ProbeModule(),
            new DigitalBrain.UI.UiModule(),
            new DigitalBrain.Introspection.IntrospectionModule(),
        ];
        var builder = new InProcessTestClusterBuilder(SiloCount);
        builder.ConfigureSilo((options, silo) =>
        {
            var moduleIndex = 0;
            foreach (var module in modules)
            {
                silo.Configuration[$"DigitalBrain:Modules:{moduleIndex}"] = module.Id.Value;
                moduleIndex++;
            }

            silo.Configuration["DigitalBrain:Security:StateProtectionKey"] =
                Convert.ToBase64String(new byte[32]);

            DigitalBrainRuntime.Add(silo, modules);
            silo.UseInMemoryReminderService();
            silo.Services.AddSingleton(_sharedJournalStorage);
            silo.Services.Configure<SiloMessagingOptions>(
                messaging => messaging.ResponseTimeout = SaturatedMachineResponseTimeout);
        });
        builder.ConfigureClient(client =>
        {
            foreach (var module in modules)
            {
                module.PrepareSerialization(client.Services);
            }

            var capabilities = ActiveCapabilityCatalog.Create(modules);
            client.Services.AddSingleton(capabilities);
            client.Services.AddSingleton(ActiveModuleContractTypeMap.Create(modules, capabilities));
            client.Services.Configure<ClientMessagingOptions>(
                messaging => messaging.ResponseTimeout = SaturatedMachineResponseTimeout);
        });

        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        var cluster = Interlocked.Exchange(ref _cluster, null);
        if (cluster is null)
        {
            return;
        }

        try
        {
            await cluster.StopAllSilosAsync();
        }
        finally
        {
            await cluster.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class BrainCollection : ICollectionFixture<BrainClusterFixture>
{
    public const string Name = "brain";
}
