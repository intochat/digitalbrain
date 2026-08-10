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
        // The AI contracts assembly is deliberately absent from the contracts list: the harness
        // runs two IAgent grains (scriptedagent, wiringagent), which a reflected IAgent catalog
        // entry would turn into an ambiguous grain-type mapping.
        var modules = new ModuleAssemblies(
            [
                typeof(ProbeModule).Assembly,
                typeof(DigitalBrain.Abstractions.ISynapseGraph).Assembly,
                typeof(DigitalBrain.Chat.SendMessage).Assembly,
                typeof(DigitalBrain.Introspection.ReadTopologyRequest).Assembly,
                typeof(DigitalBrain.Time.StartTimer).Assembly,
                typeof(DigitalBrain.Modules.Sdk.Mcp.IMcp).Assembly,
            ],
            [
                typeof(ProbeModule).Assembly,
                typeof(DigitalBrain.UI.UiModule).Assembly,
                typeof(DigitalBrain.Introspection.IntrospectionNeuron).Assembly,
                typeof(DigitalBrain.AI.Agent).Assembly,
                typeof(DigitalBrain.Time.TimerNeuron).Assembly,
                typeof(DigitalBrain.Modules.Sdk.Mcp.IMcp).Assembly,
            ]);
        var builder = new InProcessTestClusterBuilder(SiloCount);
        builder.ConfigureSilo((options, silo) =>
        {
            silo.Configuration["DigitalBrain:Security:StateProtectionKey"] =
                Convert.ToBase64String(new byte[32]);

            DigitalBrainRuntime.Add(silo, modules);
            silo.UseInMemoryReminderService();
            silo.Services.AddSingleton(_sharedJournalStorage);
            silo.Services.AddKeyedSingleton<Microsoft.Extensions.AI.IChatClient>(
                typeof(DigitalBrain.AI.Ollama.Gemma4), (_, _) => new ScriptedGemmaChatClient());
            silo.Services.AddSingleton<DigitalBrain.Modules.Sdk.Mcp.IMcpToolTransport>(
                new FakeMcpTransport());
            silo.Services.AddSingleton(new DigitalBrain.Modules.Sdk.Mcp.McpServerDefinition(
                "crm",
                "Test CRM",
                new Uri("http://localhost:1/mcp"),
                "DigitalBrain:TestCrm",
                ["mcp_api"],
                requiresClientSecret: false));
            silo.Services.Configure<SiloMessagingOptions>(
                messaging => messaging.ResponseTimeout = SaturatedMachineResponseTimeout);
        });
        builder.ConfigureClient(client =>
        {
            ModelPayloadSerialization.AddModelPayloadSerialization(client.Services);

            var capabilities = ActiveCapabilityCatalog.Create(DigitalBrainRuntime.ManifestsOf(modules));
            client.Services.AddSingleton(capabilities);
            client.Services.AddSingleton(
                ActiveModuleContractTypeMap.Create(
                    modules.Contracts.Concat(modules.Implementations),
                    capabilities));
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
