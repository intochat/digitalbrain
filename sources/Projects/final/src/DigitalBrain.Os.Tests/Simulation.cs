using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Kernel;
using DigitalBrain.Ino.Experiences;
using DigitalBrain.Hosting.DigitalBrain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Journaling;
using Orleans.TestingHost;

namespace DigitalBrain.Os;

public static class Simulation
{
    public static async Task<(TestCluster Cluster, IGrainFactory Grains, IDigitalBrain Brain)> StartAsync()
    {
        var cluster = await CreateDeployedTestClusterAsync();
        var grains = cluster.Client;
        var brain = grains.GetGrain<IDigitalBrain>(Brain.WellKnownKey);
        return (cluster, grains, brain);
    }

    private static async Task<TestCluster> CreateDeployedTestClusterAsync()
    {
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.AddSiloBuilderConfigurator<MinimalSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TimelineClientConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }

    private sealed class TimelineClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(Microsoft.Extensions.Configuration.IConfiguration configuration, IClientBuilder clientBuilder) =>
            clientBuilder.ConfigureDigitalBrainClientDefaults();
    }

    private sealed class MinimalSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder silo)
        {
            var setup = new TestSetup();
            silo.Services.AddSingleton<Setup>(setup);
            silo.ConfigureDigitalBrainDefaults();
            silo.Services.AddKeyedSingleton<IChatClient>("gemma", (_, _) => new ReliableDemoChatClient());
            silo.Services.AddSingleton(sp => sp.GetRequiredKeyedService<IChatClient>("gemma"));
            silo.Services.AddSingleton<Func<byte[], Task<string>>>(_ => _ => Task.FromResult("This is a transcribed voice message for testing the recorder to LLM flow."));
        }
    }
}
