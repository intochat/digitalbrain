using System.Reflection;
using Brain.Client;
using Brain.Contracts;
using DigitalBrain.AI;
using Orleans;
using Orleans.Runtime;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Tests.Client;

public sealed class BrainClientTests : IClassFixture<BrainClientClusterFixture>
{
    private readonly BrainClientClusterFixture _fixture;

    public BrainClientTests(BrainClientClusterFixture fixture) => _fixture = fixture;

    [Fact]
    public void Get_returns_cluster_client_grain_directly()
    {
        var brain = new Brain.Client.Brain(_fixture.Cluster.Client);
        var agent = brain.Get<IGpt56>(new OrganizationId("org-1"), new SpaceId("space-1"), "gpt-1");

        Assert.NotNull(agent);
        Assert.IsAssignableFrom<IGpt56>(agent);
        Assert.IsAssignableFrom<IAddressable>(agent);

        var expectedKey = NeuronIdentity.Derive(typeof(IGpt56), new OrganizationId("org-1"), new SpaceId("space-1"), "gpt-1");
        Assert.Equal(expectedKey, ((IAddressable)agent).GetGrainId().Key.ToString());
        Assert.DoesNotContain("DispatchProxy", agent.GetType().FullName, StringComparison.Ordinal);
    }

    [Fact]
    public void Grain_keys_include_organization_space_contract_and_instance()
    {
        var key = NeuronIdentity.Derive(typeof(IGroupChat), new OrganizationId("acme"), new SpaceId("desk"), "chat-9");
        Assert.Equal("acme|desk|chat.group.v1/chat-9", key);
    }

    [Fact]
    public async Task StartDiscussion_creates_command_synapse_and_invokes_group_chat()
    {
        var brain = new Brain.Client.Brain(_fixture.Cluster.Client);
        var gpt = brain.Get<IGpt56>(new OrganizationId("org-1"), new SpaceId("space-1"), "gpt-1");
        var grok = brain.Get<IGrok45>(new OrganizationId("org-1"), new SpaceId("space-1"), "grok-1");
        var chat = brain.Get<IGroupChat>(new OrganizationId("org-1"), new SpaceId("space-1"), "chat-1");

        var receipt = await chat.StartDiscussion(
            topic: "hello",
            gpt: gpt,
            grok: grok,
            organizationId: new OrganizationId("org-1"),
            principalId: new PrincipalId("principal-1"),
            spaceId: new SpaceId("space-1"));

        var probe = _fixture.Cluster.GrainFactory.GetGrain<IGroupChatTestProbe>(
            ((IAddressable)chat).GetGrainId().Key.ToString());

        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.Equal("hello", await probe.GetLastTopicAsync());
        Assert.Equal(((IAddressable)gpt).GetGrainId().Key.ToString(), await probe.GetLastGptKeyAsync());
        Assert.Equal(((IAddressable)grok).GetGrainId().Key.ToString(), await probe.GetLastGrokKeyAsync());
    }

    [Fact]
    public void Client_contains_no_proxy_generic_invoke_json_or_scanning()
    {
        var brainType = typeof(Brain.Client.Brain);
        Assert.False(typeof(DispatchProxy).IsAssignableFrom(brainType));

        foreach (var method in brainType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly | BindingFlags.Static))
        {
            Assert.DoesNotContain("Invoke", method.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Proxy", method.Name, StringComparison.OrdinalIgnoreCase);
        }

        var sourcePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Brain.Client");
        sourcePath = Path.GetFullPath(sourcePath);
        Assert.True(Directory.Exists(sourcePath), sourcePath);
        foreach (var file in Directory.EnumerateFiles(sourcePath, "*.cs"))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("DispatchProxy", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DynamicInvoke", source, StringComparison.Ordinal);
            Assert.DoesNotContain("JsonSerializer", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AppDomain", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GetAssemblies", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Assembly.Load", source, StringComparison.Ordinal);
        }
    }
}

public sealed class BrainClientClusterFixture : IDisposable
{
    public TestCluster Cluster { get; }

    public BrainClientClusterFixture()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        Cluster = builder.Build();
        Cluster.Deploy();
    }

    public void Dispose()
    {
        Cluster.StopAllSilos();
        Cluster.Dispose();
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
        }
    }
}

public sealed class Gpt56TestGrain : Grain, IGpt56
{
    public Task<string> GetIdentityAsync() => Task.FromResult(this.GetPrimaryKeyString());
}

public sealed class Grok45TestGrain : Grain, IGrok45
{
    public Task<string> GetIdentityAsync() => Task.FromResult(this.GetPrimaryKeyString());
}

[Alias("brain.tests.IGroupChatTestProbe")]
public interface IGroupChatTestProbe : IGroupChat
{
    [Alias("GetLastTopicAsync")]
    Task<string?> GetLastTopicAsync();

    [Alias("GetLastGptKeyAsync")]
    Task<string?> GetLastGptKeyAsync();

    [Alias("GetLastGrokKeyAsync")]
    Task<string?> GetLastGrokKeyAsync();

    [Alias("GetLastOrganizationIdAsync")]
    Task<string?> GetLastOrganizationIdAsync();

    [Alias("GetLastSpaceIdAsync")]
    Task<string?> GetLastSpaceIdAsync();

    [Alias("GetLastPrincipalIdAsync")]
    Task<string?> GetLastPrincipalIdAsync();
}

public sealed class GroupChatTestGrain : Grain, IGroupChatTestProbe
{
    private string? _topic;
    private string? _gptKey;
    private string? _grokKey;
    private string? _organizationId;
    private string? _spaceId;
    private string? _principalId;

    public Task<CommandReceipt> StartDiscussionAsync(CommandSynapse<StartDiscussion> command)
    {
        _topic = command.Payload.Topic;
        _gptKey = command.Payload.GptKey;
        _grokKey = command.Payload.GrokKey;
        _organizationId = command.Metadata.OrganizationId.Value;
        _spaceId = command.Metadata.SpaceId.Value;
        _principalId = command.Metadata.PrincipalId.Value;
        return Task.FromResult(new CommandReceipt(command.Metadata.CommandId, CommandReceiptStatus.Accepted, 1, null, null));
    }

    public Task<CommandReceipt> ApplyUiActionAsync(CommandSynapse<UiActionRequest> command) =>
        Task.FromResult(new CommandReceipt(command.Metadata.CommandId, CommandReceiptStatus.Rejected, 0, BrainErrors.RevisionConflict, "not implemented"));

    public Task<UiSurfaceSnapshot> GetSurfaceAsync() =>
        Task.FromResult(new UiSurfaceSnapshot(new UiSurface("group-chat", 0, [])));

    public Task<string?> GetLastTopicAsync() => Task.FromResult(_topic);
    public Task<string?> GetLastGptKeyAsync() => Task.FromResult(_gptKey);
    public Task<string?> GetLastGrokKeyAsync() => Task.FromResult(_grokKey);
    public Task<string?> GetLastOrganizationIdAsync() => Task.FromResult(_organizationId);
    public Task<string?> GetLastSpaceIdAsync() => Task.FromResult(_spaceId);
    public Task<string?> GetLastPrincipalIdAsync() => Task.FromResult(_principalId);
}
