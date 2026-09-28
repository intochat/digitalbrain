using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.Discovery;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AgentDirectoryFacts
{
    private static readonly AgentDefinition Researcher = new()
    {
        DisplayName = "Researcher",
        Description = "Researches companies on the web.",
        Capabilities = ["company research"],
        RoutingExamples = ["find out who owns this company"],
        Discoverable = true,
    };

    [Fact]
    public async Task ADiscoverableAgentIsListedAndSearchableUntilItStopsBeingDiscoverable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(ct);
        var agent = brain.Get<IAgent>("researcher");
        var directory = brain.Get<IAgentDirectory>(AgentDirectoryGrains.Key);

        await agent.Configure(Researcher, 0, ct);

        Assert.Equal("Researcher", Assert.Single(await directory.List()).DisplayName);
        var documents = await brain.SiloServices.GetServices<ICapabilitySource>().Single().Read(ct);
        var document = Assert.Single(documents);
        Assert.Equal(CapabilityKind.Agent, document.Kind);
        Assert.Contains("company research", document.Description, StringComparison.Ordinal);
        Assert.Equal([AgentDelegationToolName], document.Tools);

        await agent.Configure(Researcher with { Discoverable = false }, (await agent.GetState(ct)).Revision, ct);

        Assert.Empty(await directory.List());
    }

    [Fact]
    public async Task DelegationReachesOnlyListedAgents()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(ct);
        await brain.Get<IAgent>("researcher").Configure(Researcher, 0, ct);
        var send = brain.SiloServices.GetServices<IAgentToolFactory>()
            .SelectMany(factory => factory.Create(() => new("scope", "run", "call")))
            .Single(tool => tool.Name == AgentDelegationToolName);

        var listed = JsonSerializer.Serialize(await send.InvokeAsync(new() { ["agentId"] = "researcher", ["request"] = "who owns Contoso?" }, ct));
        var unlisted = JsonSerializer.Serialize(await send.InvokeAsync(new() { ["agentId"] = "someone-else", ["request"] = "hello" }, ct));

        Assert.Contains("Contoso is owned by Fabrikam", listed, StringComparison.Ordinal);
        Assert.Contains("is not a listed agent", unlisted, StringComparison.Ordinal);
    }

    private const string AgentDelegationToolName = "send_to_agent";

    private static Task<UnitBrain> Start(CancellationToken ct)
        => UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IChatClient>(new FixedChatClient("Contoso is owned by Fabrikam.")))
            .StartAsync(ct);

    private sealed class FixedChatClient(string answer) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
