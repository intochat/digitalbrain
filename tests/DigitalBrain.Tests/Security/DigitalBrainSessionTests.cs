using DigitalBrain;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Security;

public sealed class DigitalBrainSessionTests
{
    [Fact]
    public async Task Session_factory_resolves_from_client_services_and_binds_the_owner()
    {
        await using var cluster = await SessionCluster.CreateAsync();
        var sessions = cluster.ServiceProvider.GetRequiredService<DigitalBrainSessionFactory>();

        await using var session = sessions.Create(new BrainOwnerId("owner-a"));

        var neuron = session.Client.Get<ITestNeuron>();
        Assert.Equal("owner-a", neuron.GetPrimaryKeyString());
        await neuron.WriteStatusAsync(NeuronStatus.Active);
        Assert.Equal(NeuronStatus.Active, await neuron.ReadStatusAsync());
    }

    [Fact]
    public async Task Disposing_the_session_clears_the_owner_context()
    {
        await using var cluster = await SessionCluster.CreateAsync();
        var sessions = cluster.ServiceProvider.GetRequiredService<DigitalBrainSessionFactory>();
        var ownerContext = cluster.ServiceProvider.GetRequiredService<BrainOwnerContext>();

        var session = sessions.Create(new BrainOwnerId("owner-a"));
        Assert.Equal(new BrainOwnerId("owner-a"), ownerContext.Current);

        await session.DisposeAsync();

        Assert.Null(ownerContext.Current);
    }

    [Fact]
    public async Task Unvalidated_empty_owner_cannot_create_a_session()
    {
        await using var cluster = await SessionCluster.CreateAsync();
        var sessions = cluster.ServiceProvider.GetRequiredService<DigitalBrainSessionFactory>();

        Assert.ThrowsAny<ArgumentException>(() => sessions.Create(new BrainOwnerId("")));
        Assert.ThrowsAny<ArgumentException>(() => sessions.Create(new BrainOwnerId("   ")));
        Assert.ThrowsAny<ArgumentException>(() => sessions.Create(default));
    }

    [Fact]
    public async Task Session_conversations_are_owner_scoped()
    {
        await using var cluster = await SessionCluster.CreateAsync();
        var sessions = cluster.ServiceProvider.GetRequiredService<DigitalBrainSessionFactory>();

        await using var session = sessions.Create(new BrainOwnerId("owner-a"));

        var conversation = session.Client.Conversations.Open(new ConversationId("main"));
        Assert.Equal(
            ConversationKey.Encode(new BrainOwnerId("owner-a"), new ConversationId("main")),
            conversation.GetPrimaryKeyString());
    }

    [Fact]
    public async Task Overlapping_sessions_in_one_execution_context_are_rejected()
    {
        await using var cluster = await SessionCluster.CreateAsync();
        var sessions = cluster.ServiceProvider.GetRequiredService<DigitalBrainSessionFactory>();
        await using var active = sessions.Create(new BrainOwnerId("owner-a"));

        Assert.Throws<InvalidOperationException>(() =>
            sessions.Create(new BrainOwnerId("owner-b")));
        Assert.Equal(
            "owner-a",
            active.Client.Get<ITestNeuron>().GetPrimaryKeyString());
    }

    [Fact]
    public async Task Redisposing_an_old_session_does_not_clear_a_new_owner()
    {
        await using var cluster = await SessionCluster.CreateAsync();
        var sessions = cluster.ServiceProvider.GetRequiredService<DigitalBrainSessionFactory>();
        var ownerContext = cluster.ServiceProvider.GetRequiredService<BrainOwnerContext>();
        var oldSession = sessions.Create(new BrainOwnerId("owner-a"));
        await oldSession.DisposeAsync();
        await using var active = sessions.Create(new BrainOwnerId("owner-b"));

        await oldSession.DisposeAsync();

        Assert.Equal(new BrainOwnerId("owner-b"), ownerContext.Current);
        Assert.Equal("owner-b", active.Client.Get<ITestNeuron>().GetPrimaryKeyString());
    }

    private static class SessionCluster
    {
        public static async Task<TestCluster> CreateAsync()
        {
            var builder = new TestClusterBuilder();
            builder.AddSiloBuilderConfigurator<SessionSiloConfigurator>();
            builder.AddClientBuilderConfigurator<SessionClientConfigurator>();
            var cluster = builder.Build();
            await cluster.DeployAsync();
            return cluster;
        }

        private sealed class SessionSiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                siloBuilder.AddJournalStorage();
                siloBuilder.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
                siloBuilder.AddBrainKernel();
            }
        }

        private sealed class SessionClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) =>
                clientBuilder.AddDigitalBrainClient();
        }
    }
}
