extern alias McpProject;

using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Core.V2;
using DigitalBrain.Kernel.V2;
using V2RequestContext = DigitalBrain.Core.V2.RequestContext;
using IV2McpIntegrationToolGateway = McpProject::DigitalBrain.Mcp.IV2McpIntegrationToolGateway;
using V2McpAuthorizedToolCatalog = McpProject::DigitalBrain.Mcp.V2McpAuthorizedToolCatalog;
using V2McpResponseComposer = McpProject::DigitalBrain.Mcp.V2McpResponseComposer;

namespace DigitalBrain.Tests.V2;

public sealed class V2TypedAuthorizedToolCatalogTests
{
    private static readonly JsonSerializerOptions SemanticJson = CreateSemanticJson();

    [Fact]
    public async Task Proposal_must_exactly_match_the_closed_tool_and_schema()
    {
        var gateway = new RecordingGateway();
        var catalog = new V2McpAuthorizedToolCatalog(gateway);
        var proposal = new V2SemanticIntentProposal(V2SemanticProvider.Gmail, V2SemanticOperation.List);

        var mismatched = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "gmail.read"),
            Invocation(V2GmailTools.ReadThreads, proposal));
        var extraInput = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "gmail.read"),
            new V2ToolInvocation(
                V2GmailTools.ReadMessages,
                JsonElement.Parse("""{"provider":"gmail","operation":"list","limit":1,"query":"from:anyone"}""")));

        Assert.Equal(V2ToolOutcomeKind.Denied, mismatched.Kind);
        Assert.Equal(V2ToolOutcomeKind.Denied, extraInput.Kind);
        Assert.Equal(0, gateway.ProviderCallCount);
    }

    [Fact]
    public async Task Typed_provider_reads_require_a_user_principal_and_the_provider_grant()
    {
        var gateway = new RecordingGateway();
        var catalog = new V2McpAuthorizedToolCatalog(gateway);

        var missingGmailGrant = await catalog.InvokeAsync(
            Context(PrincipalKind.User),
            Invocation(V2GmailTools.ReadMessages,
                new V2SemanticIntentProposal(V2SemanticProvider.Gmail, V2SemanticOperation.List)));
        var servicePrincipal = await catalog.InvokeAsync(
            Context(PrincipalKind.Service, "gmail.read"),
            Invocation(V2GmailTools.ReadMessages,
                new V2SemanticIntentProposal(V2SemanticProvider.Gmail, V2SemanticOperation.List)));
        var missingSalesforceGrant = await catalog.InvokeAsync(
            Context(PrincipalKind.User),
            Invocation(V2SalesforceTools.ReadRecords,
                new V2SemanticIntentProposal(V2SemanticProvider.Salesforce, V2SemanticOperation.List, Entity: "Account")));

        Assert.Equal(V2ToolOutcomeKind.Denied, missingGmailGrant.Kind);
        Assert.Equal(V2ToolOutcomeKind.Denied, servicePrincipal.Kind);
        Assert.Equal(V2ToolOutcomeKind.Denied, missingSalesforceGrant.Kind);
        Assert.Equal(0, gateway.ProviderCallCount);
    }

    [Fact]
    public async Task Gmail_metadata_request_and_grounding_are_typed_bounded_and_content_free()
    {
        var gateway = new RecordingGateway
        {
            GmailMessagesResult = new V2GmailMessageListResult(
                V2GmailReadStatus.Success,
                [new V2GmailMessageMetadata(
                    "message-1",
                    "thread-1",
                    1_700_000_000_000,
                    "Ada Lovelace <ada@example.com>",
                    "ada@example.com",
                    "me@example.com",
                    ["me@example.com"],
                    "Quarterly update",
                    ["INBOX", "UNREAD"],
                    IsRead: false)],
                new V2GmailResultCoverage(1, 1, 1, 1, 0, ProviderExhausted: true, CandidateLimitReached: false))
        };
        var catalog = new V2McpAuthorizedToolCatalog(gateway);
        var context = Context(PrincipalKind.User, "gmail.read");
        var proposal = new V2SemanticIntentProposal(
            V2SemanticProvider.Gmail,
            V2SemanticOperation.List,
            Entity: "Inbox",
            Limit: 2,
            Filters: [new V2SemanticFilter("readState", V2SemanticFilterOperator.Equals, "unread")]);

        var outcome = await catalog.InvokeAsync(context, Invocation(V2GmailTools.ReadMessages, proposal));

        Assert.Equal(V2ToolOutcomeKind.Success, outcome.Kind);
        Assert.Equal(V2RequestScope.Id(context), gateway.LastOwnerScope);
        Assert.Equal(V2GmailMailboxScope.Inbox, gateway.LastGmailMessagesRequest!.Selection.Mailbox);
        Assert.Equal(V2GmailMessageReadState.Unread, gateway.LastGmailMessagesRequest.Selection.ReadState);
        Assert.Equal(2, gateway.LastGmailMessagesRequest.Limit);
        var envelope = outcome.Content!.Value.GetProperty("gmailMessages");
        Assert.Equal("message-1", envelope.GetProperty("messages")[0].GetProperty("messageId").GetString());
        Assert.Equal("ada@example.com", envelope.GetProperty("messages")[0].GetProperty("fromAddress").GetString());
        var grounding = outcome.GroundingContent!.Value.GetProperty("gmailMessages");
        Assert.Equal("unread", grounding.GetProperty("selection").GetProperty("readState").GetString());
        Assert.Equal(1, envelope.GetProperty("coverage").GetProperty("metadataRead").GetInt32());
        Assert.False(envelope.TryGetProperty("selection", out _));
        Assert.DoesNotContain("snippet", outcome.Content.Value.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("body", outcome.Content.Value.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Quarterly update", outcome.GroundingContent.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gmail_summary_requires_the_separate_content_grant_without_calling_a_provider()
    {
        var gateway = new RecordingGateway();
        var catalog = new V2McpAuthorizedToolCatalog(gateway);

        var outcome = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "gmail.read"),
            Invocation(V2GmailTools.SummarizeThread,
                new V2SemanticIntentProposal(V2SemanticProvider.Gmail, V2SemanticOperation.Summarize)));

        Assert.Equal(V2ToolOutcomeKind.Denied, outcome.Kind);
        Assert.Contains("gmail.read.content", outcome.SafeReason, StringComparison.Ordinal);
        Assert.Equal(0, gateway.ProviderCallCount);
    }

    [Fact]
    public async Task Salesforce_read_dispatches_a_compiled_schema_aware_request_and_serializes_opaque_continuation()
    {
        const string continuation = "7c3d4268c1a4455c87f942bb7351b67e";
        var gateway = new RecordingGateway
        {
            SalesforceResult = new V2SalesforceReadResult(
                V2SalesforceReadStatus.Success,
                """{"Entity":"Opportunity","Records":[{"Entity":"Opportunity","RecordId":"006000000000001","Fields":{"Name":"Renewal"}}]}""",
                Scope: new V2SalesforceReadScope("user", "org", "sf-user"),
                Continuation: new V2SalesforceContinuation(continuation, "user", "org"),
                ReturnedCount: 1,
                TotalSize: 2)
        };
        var catalog = new V2McpAuthorizedToolCatalog(gateway);
        var proposal = new V2SemanticIntentProposal(
            V2SemanticProvider.Salesforce,
            V2SemanticOperation.List,
            Entity: "Opportunity",
            Limit: 4,
            Filters: [new V2SemanticFilter("open", V2SemanticFilterOperator.Equals, "true")],
            Sorts: [new V2SemanticSort("Close Date", V2SemanticSortDirection.Descending)]);

        var outcome = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "salesforce.read"),
            Invocation(V2SalesforceTools.ReadRecords, proposal));

        Assert.Equal(V2ToolOutcomeKind.Success, outcome.Kind);
        Assert.Equal("Opportunity", gateway.LastSalesforceReadRequest!.Entity.Label);
        Assert.Equal(4, gateway.LastSalesforceReadRequest.Limit);
        var filter = Assert.Single(gateway.LastSalesforceReadRequest.Filters!);
        Assert.Equal("Is Closed", filter.Field.Label);
        Assert.Equal("false", filter.Value);
        Assert.Equal("Close Date", Assert.Single(gateway.LastSalesforceReadRequest.Sorts!).Field.Label);
        Assert.False(outcome.Content!.Value.TryGetProperty("continuation", out _));
        Assert.Equal(continuation, outcome.GroundingContent!.Value.GetProperty("continuation").GetString());
        Assert.Equal("006000000000001", outcome.Content.Value.GetProperty("salesforceRecords")
            .GetProperty("Records")[0].GetProperty("RecordId").GetString());
        Assert.DoesNotContain("Renewal", outcome.GroundingContent.Value.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("006000000000001", outcome.GroundingContent.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Salesforce_next_page_replays_the_persisted_opaque_continuation_verbatim()
    {
        const string continuation = "91f92dbdc54a490392b8801accd61b83";
        var gateway = new RecordingGateway
        {
            SalesforceResult = new V2SalesforceReadResult(V2SalesforceReadStatus.Success, "[]")
        };
        var grounding = new V2ToolGrounding(
            V2SalesforceTools.ReadRecords,
            JsonSerializer.SerializeToElement(new { continuation }));
        var store = new SnapshotConversationStore(new V2InoConversationSnapshot(
            "conversation",
            1,
            [],
            [new V2InoConversationOperation(
                "command",
                "list accounts",
                V2InoConversationStates.Succeeded,
                null,
                false,
                DateTimeOffset.UtcNow,
                Groundings: [grounding])]));
        var catalog = new V2McpAuthorizedToolCatalog(gateway, store);

        var outcome = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "salesforce.read"),
            Invocation(V2SalesforceTools.ContinueRecords,
                new V2SemanticIntentProposal(V2SemanticProvider.Salesforce, V2SemanticOperation.NextPage)));

        Assert.Equal(V2ToolOutcomeKind.Success, outcome.Kind);
        Assert.Equal(continuation, gateway.LastSalesforceContinuationRequest!.Value);
        Assert.Equal(1, gateway.ProviderCallCount);
    }

    [Fact]
    public async Task Salesforce_mutation_preview_is_local_and_never_calls_a_provider()
    {
        var gateway = new RecordingGateway();
        var catalog = new V2McpAuthorizedToolCatalog(gateway);
        var proposal = new V2SemanticIntentProposal(
            V2SemanticProvider.Salesforce,
            V2SemanticOperation.MutationPreview,
            Entity: "Account",
            SearchText: "Acme",
            Filters: [new V2SemanticFilter("Industry", V2SemanticFilterOperator.Set, "Technology")]);

        var denied = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "salesforce.read"),
            Invocation(V2SalesforceTools.PreviewMutation, proposal));
        var outcome = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "salesforce.read", "salesforce.mutation.preview"),
            Invocation(V2SalesforceTools.PreviewMutation, proposal));

        Assert.Equal(V2ToolOutcomeKind.Denied, denied.Kind);
        Assert.Equal(V2ToolOutcomeKind.Success, outcome.Kind);
        var preview = outcome.Content!.Value.GetProperty("salesforceMutationPreview");
        Assert.Equal("previewOnly", preview.GetProperty("status").GetString());
        Assert.Equal("Technology", preview.GetProperty("changes")[0].GetProperty("value").GetString());
        Assert.Equal(0, gateway.ProviderCallCount);
    }

    [Fact]
    public async Task Gmail_previous_reuses_the_persisted_candidate_ids_instead_of_rebinding_the_mailbox()
    {
        var firstGateway = new RecordingGateway
        {
            GmailMessagesResult = new V2GmailMessageListResult(
                V2GmailReadStatus.Success,
                [new V2GmailMessageMetadata(
                    "message-1", "thread-1", 3_000, "One <one@example.com>", "one@example.com",
                    null, [], "One", [], true)],
                new V2GmailResultCoverage(1, 3, 3, 3, 0, true, false),
                StableCandidateMessageIds: ["message-1", "message-2", "message-3"])
        };
        var context = Context(PrincipalKind.User, "gmail.read");
        var first = await new V2McpAuthorizedToolCatalog(firstGateway).InvokeAsync(
            context,
            Invocation(V2GmailTools.ReadMessages,
                new V2SemanticIntentProposal(V2SemanticProvider.Gmail, V2SemanticOperation.List)));
        var store = Store(new V2ToolGrounding(V2GmailTools.ReadMessages, first.GroundingContent!.Value));
        var nextGateway = new RecordingGateway();

        await new V2McpAuthorizedToolCatalog(nextGateway, store).InvokeAsync(
            context,
            Invocation(V2GmailTools.ReadMessages, new V2SemanticIntentProposal(
                V2SemanticProvider.Gmail,
                V2SemanticOperation.Previous,
                Ordinal: 1,
                Reference: V2SemanticReference.LatestProviderResult)));

        Assert.Equal(1, nextGateway.LastGmailMessagesRequest!.Offset);
        Assert.Equal(["message-1", "message-2", "message-3"],
            nextGateway.LastGmailMessagesRequest.Selection.PinnedMessageIds!);
    }

    [Fact]
    public async Task Gmail_ordinal_followup_reads_one_stable_result_at_the_requested_position()
    {
        var context = Context(PrincipalKind.User, "gmail.read");
        var selection = new V2GmailMessageSelection(
            PinnedMessageIds: ["message-1", "message-2"],
            MaxCandidates: 2);
        var store = Store(new V2ToolGrounding(
            V2GmailTools.ReadMessages,
            JsonSerializer.SerializeToElement(new
            {
                gmailMessages = new
                {
                    resultMessageIds = new[] { "message-1", "message-2" },
                    selection,
                    nextOffset = 2,
                    hasMore = false
                }
            }, SemanticJson)));
        var gateway = new RecordingGateway();

        var outcome = await new V2McpAuthorizedToolCatalog(gateway, store).InvokeAsync(
            context,
            Invocation(V2GmailTools.ReadMessages, new V2SemanticIntentProposal(
                V2SemanticProvider.Gmail,
                V2SemanticOperation.List,
                Limit: 2,
                Ordinal: 2,
                Reference: V2SemanticReference.LatestProviderResult)));

        Assert.Equal(V2ToolOutcomeKind.Success, outcome.Kind);
        Assert.Equal(1, gateway.LastGmailMessagesRequest!.Offset);
        Assert.Equal(1, gateway.LastGmailMessagesRequest.Limit);
        Assert.Equal(["message-1", "message-2"],
            gateway.LastGmailMessagesRequest.Selection.PinnedMessageIds!);
    }

    [Fact]
    public async Task Gmail_pinned_page_advances_over_consumed_ids_when_refetched_candidates_are_unavailable()
    {
        var context = Context(PrincipalKind.User, "gmail.read");
        var selection = new V2GmailMessageSelection(
            PinnedMessageIds: ["message-1", "message-2", "message-3"],
            MaxCandidates: 3);
        var store = Store(new V2ToolGrounding(
            V2GmailTools.ReadMessages,
            JsonSerializer.SerializeToElement(new
            {
                gmailMessages = new
                {
                    resultMessageIds = new[] { "message-1" },
                    selection,
                    nextOffset = 1,
                    hasMore = true
                }
            }, SemanticJson)));
        var gateway = new RecordingGateway
        {
            GmailMessagesResult = new V2GmailMessageListResult(
                V2GmailReadStatus.Success,
                [],
                new V2GmailResultCoverage(0, 2, 0, 0, 2, true, false),
                StableCandidateMessageIds: ["message-1", "message-2", "message-3"])
        };

        var outcome = await new V2McpAuthorizedToolCatalog(gateway, store).InvokeAsync(
            context,
            Invocation(V2GmailTools.ReadMessages, new V2SemanticIntentProposal(
                V2SemanticProvider.Gmail,
                V2SemanticOperation.Previous,
                Limit: 2,
                Reference: V2SemanticReference.LatestProviderResult)));

        Assert.Equal(V2ToolOutcomeKind.Success, outcome.Kind);
        Assert.Equal(1, gateway.LastGmailMessagesRequest!.Offset);
        var grounding = outcome.GroundingContent!.Value.GetProperty("gmailMessages");
        Assert.Equal(3, grounding.GetProperty("nextOffset").GetInt32());
        Assert.False(grounding.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Salesforce_related_read_uses_the_minimal_grounded_record_id()
    {
        var firstGateway = new RecordingGateway
        {
            SalesforceResult = new V2SalesforceReadResult(
                V2SalesforceReadStatus.Success,
                """{"Entity":"Opportunity","Records":[{"Entity":"Opportunity","RecordId":"006000000000001","Fields":{"Name":"Renewal"}}]}""",
                ReturnedCount: 1)
        };
        var context = Context(PrincipalKind.User, "salesforce.read");
        var first = await new V2McpAuthorizedToolCatalog(firstGateway).InvokeAsync(
            context,
            Invocation(V2SalesforceTools.ReadRecords, new V2SemanticIntentProposal(
                V2SemanticProvider.Salesforce,
                V2SemanticOperation.List,
                Entity: "Opportunity")));
        var store = Store(new V2ToolGrounding(V2SalesforceTools.ReadRecords, first.GroundingContent!.Value));
        var relatedGateway = new RecordingGateway();

        await new V2McpAuthorizedToolCatalog(relatedGateway, store).InvokeAsync(
            context,
            Invocation(V2SalesforceTools.ReadRecords, new V2SemanticIntentProposal(
                V2SemanticProvider.Salesforce,
                V2SemanticOperation.Related,
                Entity: "Contact",
                Reference: V2SemanticReference.LatestProviderResult)));

        Assert.Equal("006000000000001", relatedGateway.LastSalesforceReadRequest!.RelatedTo!.RecordId);
        Assert.Equal("Opportunity", relatedGateway.LastSalesforceReadRequest.RelatedTo.Entity.Label);
    }

    [Theory]
    [InlineData(V2SemanticOperation.Details)]
    [InlineData(V2SemanticOperation.Related)]
    public async Task Salesforce_followup_requires_one_grounded_record_or_an_explicit_ordinal(
        V2SemanticOperation operation)
    {
        var context = Context(PrincipalKind.User, "salesforce.read");
        var store = Store(new V2ToolGrounding(
            V2SalesforceTools.ReadRecords,
            JsonSerializer.SerializeToElement(new
            {
                entity = "Account",
                recordIds = new[] { "001000000000001", "001000000000002" },
                resultCount = 2
            }, SemanticJson)));
        var ambiguousGateway = new RecordingGateway();
        var entity = operation == V2SemanticOperation.Related ? "Contact" : "Account";

        var ambiguous = await new V2McpAuthorizedToolCatalog(ambiguousGateway, store).InvokeAsync(
            context,
            Invocation(V2SalesforceTools.ReadRecords, new V2SemanticIntentProposal(
                V2SemanticProvider.Salesforce,
                operation,
                Entity: entity,
                Reference: V2SemanticReference.LatestProviderResult)));

        Assert.Equal(V2ToolOutcomeKind.PermanentFailure, ambiguous.Kind);
        Assert.Contains("multiple records", ambiguous.SafeReason, StringComparison.Ordinal);
        Assert.Equal(0, ambiguousGateway.ProviderCallCount);

        var ordinalGateway = new RecordingGateway();
        var selected = await new V2McpAuthorizedToolCatalog(ordinalGateway, store).InvokeAsync(
            context,
            Invocation(V2SalesforceTools.ReadRecords, new V2SemanticIntentProposal(
                V2SemanticProvider.Salesforce,
                operation,
                Entity: entity,
                Ordinal: 2,
                Reference: V2SemanticReference.LatestProviderResult)));

        Assert.Equal(V2ToolOutcomeKind.Success, selected.Kind);
        var resolved = operation == V2SemanticOperation.Related
            ? ordinalGateway.LastSalesforceReadRequest!.RelatedTo
            : ordinalGateway.LastSalesforceReadRequest!.Record;
        Assert.Equal("001000000000002", resolved!.RecordId);
        Assert.Equal("Account", resolved.Entity.Label);
        Assert.Equal(1, ordinalGateway.ProviderCallCount);
    }

    [Fact]
    public async Task Cross_provider_match_returns_clarification_when_salesforce_is_ambiguous()
    {
        var gateway = new RecordingGateway
        {
            GmailMessagesResult = new V2GmailMessageListResult(
                V2GmailReadStatus.Success,
                [new V2GmailMessageMetadata(
                    "message-1", "thread-1", 1_700_000_000_000,
                    "Ada <ada@example.com>", "ada@example.com", null, [], null, [], true)],
                new V2GmailResultCoverage(1, 1, 1, 1, 0, true, false)),
            SalesforceResult = new V2SalesforceReadResult(
                V2SalesforceReadStatus.Success,
                """[{"Id":"001-a"},{"Id":"001-b"}]""",
                ReturnedCount: 2)
        };
        var catalog = new V2McpAuthorizedToolCatalog(gateway);
        var proposal = new V2SemanticIntentProposal(
            V2SemanticProvider.CrossProvider,
            V2SemanticOperation.Match,
            Entity: "Account",
            Limit: 3,
            Reference: V2SemanticReference.LatestGmailSender);

        var outcome = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "gmail.read", "salesforce.read"),
            Invocation(V2CrossProviderTools.MatchSalesforceAccountToGmailSender, proposal));

        Assert.Equal(V2ToolOutcomeKind.PermanentFailure, outcome.Kind);
        Assert.Contains("More than one Salesforce account", outcome.SafeReason, StringComparison.Ordinal);
        Assert.Equal("ada@example.com", gateway.LastSalesforceSearchRequest!.SearchText);
        Assert.Equal("Account", Assert.Single(gateway.LastSalesforceSearchRequest.Entities!).Label);
        Assert.Equal(2, gateway.ProviderCallCount);
    }

    [Fact]
    public async Task Deterministic_provider_composer_omits_external_addresses_from_provider_text()
    {
        var content = JsonSerializer.SerializeToElement(new
        {
            salesforceRecords = new[] { new { Name = "Acme", Website = "https://evil.example/path" } }
        });

        var text = await new V2McpResponseComposer().ComposeAsync(
            Context(PrincipalKind.User, "salesforce.read"),
            new V2ModelResponse(string.Empty, "deterministic-tool-response", true),
            [new V2ToolOutcome(V2ToolOutcomeKind.Success, content)]);

        Assert.DoesNotContain("evil.example", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[external address omitted]", text, StringComparison.Ordinal);
    }

    private static V2ToolInvocation Invocation(string toolId, V2SemanticIntentProposal proposal) =>
        new(toolId, JsonSerializer.SerializeToElement(proposal, SemanticJson));

    private static V2RequestContext Context(PrincipalKind principalKind, params string[] grants) => new(
        new TenantId("tenant"),
        new WorkspaceId("workspace"),
        new PrincipalRef("principal", principalKind),
        "session",
        AuthAssurance.Password,
        "correlation",
        "idempotency",
        grants.ToHashSet(StringComparer.Ordinal));

    private static SnapshotConversationStore Store(V2ToolGrounding grounding) => new(new V2InoConversationSnapshot(
        "conversation",
        1,
        [],
        [new V2InoConversationOperation(
            "command",
            "provider read",
            V2InoConversationStates.Succeeded,
            null,
            false,
            DateTimeOffset.UtcNow,
            Groundings: [grounding])]));

    private static JsonSerializerOptions CreateSemanticJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed class RecordingGateway : IV2McpIntegrationToolGateway
    {
        private static readonly V2GmailResultCoverage EmptyCoverage = new(0, 0, 0, 0, 0, true, false);

        public int ProviderCallCount { get; private set; }
        public string? LastOwnerScope { get; private set; }
        public V2GmailMessageListRequest? LastGmailMessagesRequest { get; private set; }
        public V2SalesforceRecordReadRequest? LastSalesforceReadRequest { get; private set; }
        public V2SalesforceSearchRequest? LastSalesforceSearchRequest { get; private set; }
        public V2SalesforceContinuationRequest? LastSalesforceContinuationRequest { get; private set; }
        public V2GmailMessageListResult GmailMessagesResult { get; init; } =
            new(V2GmailReadStatus.Success, [], EmptyCoverage);
        public V2SalesforceReadResult SalesforceResult { get; init; } =
            new(V2SalesforceReadStatus.Success, "[]");

        public Task<V2GmailReadResult> ReadIncomingAtOffsetAsync(
            string ownerScope,
            V2GmailReadRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            return Task.FromResult(new V2GmailReadResult(V2GmailReadStatus.Unavailable));
        }

        public Task<V2SalesforceReadResult> ReadSalesforceAsync(
            string ownerScope,
            string toolId,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            return Task.FromResult(SalesforceResult);
        }

        public Task<V2GmailMessageListResult> ReadGmailMessagesAsync(
            string ownerScope,
            V2GmailMessageListRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            LastGmailMessagesRequest = request;
            return Task.FromResult(GmailMessagesResult);
        }

        public Task<V2GmailMailboxOverviewResult> ReadGmailMailboxOverviewAsync(
            string ownerScope,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            return Task.FromResult(new V2GmailMailboxOverviewResult(V2GmailReadStatus.Success));
        }

        public Task<V2GmailThreadListResult> ReadGmailThreadsAsync(
            string ownerScope,
            V2GmailThreadListRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            return Task.FromResult(new V2GmailThreadListResult(V2GmailReadStatus.Success, [], EmptyCoverage));
        }

        public Task<V2SalesforceReadResult> DiscoverSalesforceObjectsAsync(
            string ownerScope,
            V2SalesforceDiscoveryRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            return Task.FromResult(SalesforceResult);
        }

        public Task<V2SalesforceReadResult> ReadSalesforceRecordsAsync(
            string ownerScope,
            V2SalesforceRecordReadRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            LastSalesforceReadRequest = request;
            return Task.FromResult(SalesforceResult);
        }

        public Task<V2SalesforceReadResult> SearchSalesforceRecordsAsync(
            string ownerScope,
            V2SalesforceSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            LastSalesforceSearchRequest = request;
            return Task.FromResult(SalesforceResult);
        }

        public Task<V2SalesforceReadResult> AggregateSalesforceRecordsAsync(
            string ownerScope,
            V2SalesforceAggregateRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            return Task.FromResult(SalesforceResult);
        }

        public Task<V2SalesforceReadResult> ContinueSalesforceRecordsAsync(
            string ownerScope,
            V2SalesforceContinuationRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            LastSalesforceContinuationRequest = request;
            return Task.FromResult(SalesforceResult);
        }

        private void Record(string ownerScope)
        {
            ProviderCallCount++;
            LastOwnerScope = ownerScope;
        }
    }

    private sealed class SnapshotConversationStore(V2InoConversationSnapshot snapshot) : IV2InoConversationStore
    {
        public V2InoConversationSnapshot Read(V2RequestContext context) => snapshot;
        public V2InoConversationSnapshot Begin(V2RequestContext context, string commandId, string prompt) => throw new NotSupportedException();
        public V2InoConversationSnapshot Transition(V2RequestContext context, string commandId, string state) => throw new NotSupportedException();
        public V2InoConversationSnapshot Complete(
            V2RequestContext context,
            string commandId,
            string response,
            V2ToolAction? action = null,
            V2ToolGrounding? grounding = null,
            IReadOnlyList<V2ToolGrounding>? groundings = null) => throw new NotSupportedException();
        public V2InoConversationSnapshot Fail(
            V2RequestContext context,
            string commandId,
            string safeReason,
            bool retryable) => throw new NotSupportedException();
    }
}
