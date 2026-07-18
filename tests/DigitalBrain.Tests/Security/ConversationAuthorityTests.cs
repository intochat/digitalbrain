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

public sealed class ConversationAuthorityTests
{
    [Fact]
    public void Composite_key_authorization_is_selected_only_by_the_typed_conversation_marker()
    {
        Assert.True(typeof(IConversationGrain).IsInterface);
        Assert.Empty(typeof(IConversationGrain).GetMethods());
        Assert.False(typeof(IConversationGrain).IsPublic);
        Assert.False(typeof(IConversationGrain).IsNestedPublic);
        Assert.True(typeof(IConversationGrain)
            .IsAssignableFrom(typeof(DigitalBrain.Tests.Conversations.TestConversationNeuron)));
        Assert.False(typeof(IConversationGrain).IsAssignableFrom(typeof(TestNeuron)));
    }

    [Fact]
    public async Task Unmarked_neurons_keep_exact_owner_key_authorization_even_for_canonical_keys()
    {
        await using var cluster = await AuthorityCluster.CreateAsync();
        var ownerContext = cluster.ServiceProvider.GetRequiredService<BrainOwnerContext>();
        ownerContext.Current = new BrainOwnerId("owner-a");

        try
        {
            var canonicalKey = ConversationKey.Encode(new BrainOwnerId("owner-a"), new ConversationId("main"));
            var providerLeafWithCompositeKey = cluster.Client.GetGrain<ITestNeuron>(canonicalKey);

            var denied = await Assert.ThrowsAsync<BrainException>(
                () => providerLeafWithCompositeKey.ReadStatusAsync());

            Assert.Equal(NeuronFailureKind.AuthorizationDenied, denied.FailureKind);
        }
        finally
        {
            ownerContext.Current = null;
            RequestContext.Remove(nameof(BrainOwnerId));
        }
    }

    [Fact]
    public async Task Provider_leaves_keep_exact_owner_key_authorization()
    {
        await using var cluster = await AuthorityCluster.CreateAsync();
        var ownerContext = cluster.ServiceProvider.GetRequiredService<BrainOwnerContext>();
        ownerContext.Current = new BrainOwnerId("owner-a");

        try
        {
            var owned = cluster.Client.GetGrain<ITestNeuron>("owner-a");
            await owned.WriteStatusAsync(NeuronStatus.Active);
            Assert.Equal(NeuronStatus.Active, await owned.ReadStatusAsync());
        }
        finally
        {
            ownerContext.Current = null;
            RequestContext.Remove(nameof(BrainOwnerId));
        }
    }

    private static class AuthorityCluster
    {
        public static async Task<TestCluster> CreateAsync()
        {
            var builder = new TestClusterBuilder();
            builder.AddSiloBuilderConfigurator<AuthoritySiloConfigurator>();
            builder.AddClientBuilderConfigurator<AuthorityClientConfigurator>();
            var cluster = builder.Build();
            await cluster.DeployAsync();
            return cluster;
        }

        private sealed class AuthoritySiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                siloBuilder.AddJournalStorage();
                siloBuilder.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
                siloBuilder.AddBrainKernel();
            }
        }

        private sealed class AuthorityClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) =>
                clientBuilder.AddDigitalBrainClient();
        }
    }
}
