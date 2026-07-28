using DigitalBrain.AI;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class CapabilityToolSeam(ModuleFixture fixture)
{
    private const string AgentName = "assistant";
    private const string AccountId = "001AAAAAAAAAAAAAAA";
    private const string MessageId = "msg-42";
    private const string FinalReply = "Account updated.";
    private const string IdentityPrompt = "Who are you?";
    private const int SeamTimeout = 60_000;

    [Fact(Timeout = SeamTimeout, DisplayName =
        "agent instructions reach the model as the first system turn")]
    public async Task AgentInstructionsReachModelAsFirstSystemTurn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply(FinalReply);

        await test.Client.Get<IToolAgentProbe>(AgentName).Respond([new(Microsoft.Extensions.AI.ChatRole.User, IdentityPrompt)]);

        Assert.Collection(
            test.Chat().LastMessages,
            system =>
            {
                Assert.Equal(Microsoft.Extensions.AI.ChatRole.System, system.Role);
                Assert.Equal(ToolAgentProbe.ProbeInstructions, system.Text);
            },
            user =>
            {
                Assert.Equal(Microsoft.Extensions.AI.ChatRole.User, user.Role);
                Assert.Equal(IdentityPrompt, user.Text);
            });
    }

    [Fact(Timeout = SeamTimeout, DisplayName =
        "a model tool call reaches the real neuron capability with the model's own arguments")]
    public async Task ModelToolCallInvokesNeuronCapability()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var enrichment = test.Neuron<IEnrichmentProbe>(ToolAgentProbe.ProbeName);
        test.Chat().ReplyWithCapabilityCall(
            ToolAgentProbe.EnrichTool,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["accountId"] = AccountId,
                ["messageId"] = MessageId,
            });
        test.Chat().Reply(FinalReply);

        var response = await test.Client.Get<IToolAgentProbe>(AgentName).Respond(
            [new(Microsoft.Extensions.AI.ChatRole.User, "enrich my account from the latest email")]);

        var enriched = await enrichment.Outgoing.NextAsync<ProbeAccountEnriched>(cancellationToken);
        Assert.Equal(AccountId, enriched.Synapse.AccountId);
        Assert.Equal(MessageId, enriched.Synapse.MessageId);
        Assert.Equal(FinalReply, response.Text);
    }

    [Fact(Timeout = SeamTimeout, DisplayName =
        "the capability the model selected is journaled by the agent, since kernel facts exclude arguments")]
    public async Task SelectedCapabilityIsJournaled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var agent = test.Neuron<IToolAgentProbe>(AgentName);
        test.Chat().ReplyWithCapabilityCall(
            ToolAgentProbe.EnrichTool,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["accountId"] = AccountId,
                ["messageId"] = MessageId,
            });
        test.Chat().Reply(FinalReply);

        await test.Client.Get<IToolAgentProbe>(AgentName).Respond(
            [new(Microsoft.Extensions.AI.ChatRole.User, "enrich my account")]);

        var selected = await agent.Outgoing.NextAsync<CapabilityToolSelected>(cancellationToken);
        Assert.Equal(ToolAgentProbe.EnrichTool, selected.Synapse.Tool);
    }

    [Fact(Timeout = SeamTimeout, DisplayName =
        "a model that answers without selecting a capability journals no CapabilityToolSelected")]
    public async Task NoCapabilitySelectedWhenModelAnswersDirectly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var agent = test.Neuron<IToolAgentProbe>(AgentName);
        test.Chat().Reply(FinalReply);

        await test.Client.Get<IToolAgentProbe>(AgentName).Respond([new(Microsoft.Extensions.AI.ChatRole.User, "hello")]);

        var selected = await agent.Outgoing.ReadAsync<CapabilityToolSelected>(afterSequence: 0, cancellationToken);
        Assert.Empty(selected);
    }
}
