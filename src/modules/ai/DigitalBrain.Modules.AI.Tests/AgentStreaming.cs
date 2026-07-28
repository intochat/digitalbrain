using System.Text;
using DigitalBrain.AI;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class AgentStreaming(ModuleFixture fixture)
{
    private const string AgentName = "assistant";
    private const string AccountId = "001AAAAAAAAAAAAAAA";
    private const string MessageId = "msg-42";
    private const string FinalReply = "Account updated.";
    private const string EnrichPrompt = "enrich my account from the latest email";
    private const int StreamingTimeout = 60_000;

    [Fact(Timeout = StreamingTimeout, DisplayName =
        "IAgent.RespondStreaming yields updates and still journals CapabilityToolSelected")]
    public async Task StreamingAgentEmitsCapabilitySelection()
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

        var text = new StringBuilder();
        await foreach (var update in test.Client.Get<IToolAgentProbe>(AgentName)
            .RespondStreaming([new ChatMessage(ChatRole.User, EnrichPrompt)], cancellationToken))
        {
            text.Append(update.Text);
        }

        var enriched = await enrichment.Outgoing.NextAsync<ProbeAccountEnriched>(cancellationToken);
        Assert.Equal(AccountId, enriched.Synapse.AccountId);
        Assert.Equal(MessageId, enriched.Synapse.MessageId);
        Assert.Equal(FinalReply, text.ToString());
    }

    [Fact(Timeout = StreamingTimeout, DisplayName =
        "the capability the model selected over RespondStreaming is journaled once the stream drains")]
    public async Task StreamingCapabilitySelectionIsJournaled()
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

        await foreach (var _ in test.Client.Get<IToolAgentProbe>(AgentName)
            .RespondStreaming([new ChatMessage(ChatRole.User, EnrichPrompt)], cancellationToken))
        {
        }

        var selected = await agent.Outgoing.NextAsync<CapabilityToolSelected>(cancellationToken);
        Assert.Equal(ToolAgentProbe.EnrichTool, selected.Synapse.Tool);
    }
}
