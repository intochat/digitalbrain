using DigitalBrain;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Client;

public sealed class DigitalBrainClientTests
{
    [Fact]
    public async Task Get_binds_authenticated_owner_as_the_complete_grain_key()
    {
        await using var cluster = await OwnerBoundClientCluster.CreateAsync();
        var brain = new DigitalBrainClient(cluster.Client, new BrainOwnerId("owner-a"));

        var neuron = brain.Get<ITestNeuron>();

        Assert.Equal("owner-a", neuron.GetPrimaryKeyString());
    }

    [Fact]
    public async Task Session_factory_resolves_from_DI_and_creates_owner_bound_scoped_clients()
    {
        await using var cluster = await OwnerBoundClientCluster.CreateAsync();
        var sessions = cluster.Client.ServiceProvider
            .GetRequiredService<DigitalBrainSessionFactory>();

        await using (var first = sessions.Create(new BrainOwnerId("owner-a")))
        {
            Assert.Equal(
                "owner-a",
                first.Client.Get<ITestNeuron>().GetPrimaryKeyString());
        }

        await using var second = sessions.Create(new BrainOwnerId("owner-b"));
        Assert.Equal(
            "owner-b",
            second.Client.Get<ITestNeuron>().GetPrimaryKeyString());
    }

    [Fact]
    public async Task Conversation_and_role_facades_are_backed_by_an_Orleans_proxy()
    {
        await using var cluster = await OwnerBoundClientCluster.CreateAsync();
        var sessions = cluster.Client.ServiceProvider
            .GetRequiredService<DigitalBrainSessionFactory>();
        await using var session = sessions.Create(new BrainOwnerId("owner-a"));
        var conversationId = new ConversationId("conversation-a");

        var conversation = session.Client.Conversations.Open(conversationId);

        Assert.IsAssignableFrom<IAddressable>(conversation);
        Assert.IsType<FastConversationClient>(
            session.Client.Conversations.Fast(conversationId));
        Assert.IsType<BalancedConversationClient>(
            session.Client.Conversations.Balanced(conversationId));
        Assert.IsType<ReasoningConversationClient>(
            session.Client.Conversations.Reasoning(conversationId));
    }

    [Fact]
    public void Client_source_contains_no_proxy_reflection_json_or_address_routing()
    {
        var clientDirectory = FindRepositoryDirectory("kernel", "DigitalBrain.Client");
        var sources = Directory.GetFiles(clientDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();
        var joined = string.Join('\n', sources);

        Assert.DoesNotContain("DispatchProxy", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("MethodInfo", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("GetMethod", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("Neuron" + "Address", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("Parse(", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("provider/", joined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gmail/", joined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("salesforce/", joined, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryDirectory(params string[] relativeSegments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException($"Could not locate {string.Join('/', relativeSegments)} from the test output directory.");
    }
}

file static class OwnerBoundClientCluster
{
    public static async Task<TestCluster> CreateAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<OwnerBoundSiloConfigurator>();
        builder.AddClientBuilderConfigurator<OwnerBoundClientConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }

    private sealed class OwnerBoundSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddJournalStorage();
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
            siloBuilder.AddBrainKernel();
        }
    }

    private sealed class OwnerBoundClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            IClientBuilder clientBuilder) =>
            clientBuilder.AddDigitalBrainClient();
    }
}
