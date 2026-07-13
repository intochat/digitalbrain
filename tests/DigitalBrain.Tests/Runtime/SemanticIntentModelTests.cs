using System.Text.Json;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Tests.Runtime;

public sealed class SemanticIntentModelTests
{
    private const string GmailListProposal = """
        {
          "provider": "gmail",
          "operation": "list",
          "entity": "message",
          "limit": 2,
          "ordinal": 2,
          "reference": "latestProviderResult",
          "filters": [
            {
              "field": "readState",
              "operator": "equals",
              "value": "unread"
            }
          ],
          "sorts": [
            {
              "field": "receivedTime",
              "direction": "descending"
            }
          ],
          "aggregate": null,
          "timeRange": "previousWeek",
          "searchText": null,
          "clarification": null,
          "relativeDays": null
        }
        """;

    [Fact]
    public async Task Resolve_intent_uses_separate_roles_json_schema_and_descriptor_only_grounding()
    {
        const string tenantId = "tenant-secret-7dd3";
        const string workspaceId = "workspace-secret-83b1";
        const string conversationId = "conversation-secret-a914";
        const string prompt = "Bring back item two from the earlier result.";
        var grounding = new GroundingDescriptor(
            Provider: "gmail",
            ToolId: "gmail.read.messages",
            ResultCount: 3,
            HasContinuation: true,
            TurnDistance: 1);
        var chat = new RecordingStructuredChatClient(GmailListProposal);
        var grain = new ConversationModelGrain(chat);

        await grain.ResolveIntentAsync(
            new SemanticIntentRequest(tenantId, workspaceId, conversationId, prompt, [grounding]));

        Assert.Collection(
            chat.LastMessages,
            system => Assert.Equal(ChatRole.System, system.Role),
            user =>
            {
                Assert.Equal(ChatRole.User, user.Role);
                Assert.Equal(prompt, user.Text);
            });

        var modelInput = string.Join('\n', chat.LastMessages.Select(static message => message.Text));
        Assert.DoesNotContain(tenantId, modelInput, StringComparison.Ordinal);
        Assert.DoesNotContain(workspaceId, modelInput, StringComparison.Ordinal);
        Assert.DoesNotContain(conversationId, modelInput, StringComparison.Ordinal);

        const string descriptorsMarker = "Available grounding descriptors: ";
        var systemText = chat.LastMessages[0].Text!;
        Assert.Contains("Copy only constraints the user explicitly requested", systemText, StringComparison.Ordinal);
        Assert.Contains("Reference is None for a standalone request", systemText, StringComparison.Ordinal);
        Assert.Contains("one numbered item from an earlier result", systemText, StringComparison.Ordinal);
        Assert.Contains("'Who sent the second one?'", systemText, StringComparison.Ordinal);
        Assert.Contains("\"ordinal\":2", systemText, StringComparison.Ordinal);
        Assert.Contains("'list my last two emails'", systemText, StringComparison.Ordinal);
        Assert.Contains("\"limit\":2", systemText, StringComparison.Ordinal);
        Assert.Contains("\"timeRange\":\"none\"", systemText, StringComparison.Ordinal);
        Assert.Contains("RelativeDays is only an explicitly requested rolling number of days", systemText, StringComparison.Ordinal);
        Assert.Contains("\"relativeDays\":null", systemText, StringComparison.Ordinal);
        Assert.Contains("Provider None is valid only with Answer", systemText, StringComparison.Ordinal);
        Assert.Contains("'Find Acme.'", systemText, StringComparison.Ordinal);
        Assert.Contains("\"searchText\":\"Acme\"", systemText, StringComparison.Ordinal);
        var descriptorsStart = systemText.IndexOf(descriptorsMarker, StringComparison.Ordinal);
        Assert.True(descriptorsStart >= 0);
        using var descriptors = JsonDocument.Parse(systemText[(descriptorsStart + descriptorsMarker.Length)..]);
        var serializedGrounding = Assert.Single(descriptors.RootElement.EnumerateArray());
        Assert.Equal(
            ["hasContinuation", "provider", "resultCount", "toolId", "turnDistance"],
            serializedGrounding.EnumerateObject().Select(static property => property.Name).Order().ToArray());
        Assert.Equal("gmail", serializedGrounding.GetProperty("provider").GetString());
        Assert.Equal("gmail.read.messages", serializedGrounding.GetProperty("toolId").GetString());
        Assert.Equal(3, serializedGrounding.GetProperty("resultCount").GetInt32());
        Assert.True(serializedGrounding.GetProperty("hasContinuation").GetBoolean());
        Assert.Equal(1, serializedGrounding.GetProperty("turnDistance").GetInt32());

        var responseFormat = Assert.IsType<ChatResponseFormatJson>(chat.LastOptions?.ResponseFormat);
        Assert.True(responseFormat.Schema.HasValue);
        var schema = responseFormat.Schema.Value.GetRawText();
        Assert.Contains("\"provider\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"operation\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"relativeDays\"", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("tenantId", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("workspaceId", schema, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("plz show my 2nd unread mails from last wk")]
    [InlineData("Could you pull up the second unread message from the previous week?")]
    public async Task Resolve_intent_deserializes_typed_proposals_for_typos_and_paraphrases(string prompt)
    {
        var chat = new RecordingStructuredChatClient(GmailListProposal);
        var grain = new ConversationModelGrain(chat);

        var proposal = await grain.ResolveIntentAsync(Request(prompt));

        Assert.Equal(SemanticProvider.Gmail, proposal.Provider);
        Assert.Equal(SemanticOperation.List, proposal.Operation);
        Assert.Equal("message", proposal.Entity);
        Assert.Equal(2, proposal.Limit);
        Assert.Equal(2, proposal.Ordinal);
        Assert.Equal(SemanticReference.LatestProviderResult, proposal.Reference);
        Assert.Equal(SemanticTimeRange.PreviousWeek, proposal.TimeRange);
        var filter = Assert.Single(proposal.Filters!);
        Assert.Equal(new SemanticFilter("readState", SemanticFilterOperator.Equals, "unread"), filter);
        var sort = Assert.Single(proposal.Sorts!);
        Assert.Equal(new SemanticSort("receivedTime", SemanticSortDirection.Descending), sort);
        Assert.Equal(prompt, chat.LastMessages[1].Text);
    }

    [Fact]
    public async Task Resolve_intent_rejects_unknown_json_members()
    {
        const string proposalWithUnknownMember = """
            {
              "provider": "gmail",
              "operation": "list",
              "rawProviderQuery": "from:boss@example.com"
            }
            """;
        var chat = new RecordingStructuredChatClient(proposalWithUnknownMember);
        var grain = new ConversationModelGrain(chat);

        await Assert.ThrowsAsync<JsonException>(() => grain.ResolveIntentAsync(Request("Show my messages.")));
    }

    [Fact]
    public async Task Resolve_intent_propagates_cancellation_to_the_chat_client()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var chat = new RecordingStructuredChatClient(GmailListProposal);
        var grain = new ConversationModelGrain(chat);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            grain.ResolveIntentAsync(Request("Show my messages."), cancellation.Token));

        Assert.Equal(cancellation.Token, chat.LastCancellationToken);
    }

    [Fact]
    public async Task Resolve_mutation_extracts_only_the_typed_gmail_fields_under_strict_guidance()
    {
        const string response = """
            {
              "kind": "gmailSend",
              "recipient": "safe-recipient@example.com",
              "subject": "Acceptance check",
              "body": "Exact body",
              "entity": null,
              "recordId": null,
              "field": null,
              "newValue": null,
              "clarification": null
            }
            """;
        var chat = new RecordingStructuredChatClient(response);
        var grain = new ConversationModelGrain(chat);

        var proposal = await grain.ResolveMutationAsync(new SemanticMutationRequest(
            new string('a', 64),
            "conversation-not-for-model",
            SemanticProvider.Gmail,
            "Send the exact acceptance email."));

        Assert.Equal(SemanticMutationKind.GmailSend, proposal.Kind);
        Assert.Equal("safe-recipient@example.com", proposal.Recipient);
        Assert.Equal("Acceptance check", proposal.Subject);
        Assert.Equal("Exact body", proposal.Body);
        Assert.Contains("never invent, infer", chat.LastMessages[0].Text, StringComparison.Ordinal);
        Assert.Contains("bulk, multi-recipient", chat.LastMessages[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("conversation-not-for-model", chat.LastMessages[0].Text, StringComparison.Ordinal);
    }

    private static SemanticIntentRequest Request(string prompt) =>
        new("tenant-not-for-model", "workspace-not-for-model", "conversation-not-for-model", prompt, []);

    private sealed class RecordingStructuredChatClient(string responseJson) : IChatClient
    {
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public ChatOptions? LastOptions { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToArray();
            LastOptions = options;
            LastCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseJson)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Streaming is not used by the structured intent model port.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
