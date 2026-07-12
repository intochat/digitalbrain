extern alias McpProject;

using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;
using IMcpIntegrationToolGateway = McpProject::DigitalBrain.Mcp.IMcpIntegrationToolGateway;
using McpAuthorizedToolCatalog = McpProject::DigitalBrain.Mcp.McpAuthorizedToolCatalog;
using McpResponseComposer = McpProject::DigitalBrain.Mcp.McpResponseComposer;

namespace DigitalBrain.Tests.Runtime;

public sealed class TypedAuthorizedToolCatalogTests
{
    private static readonly JsonSerializerOptions SemanticJson = CreateSemanticJson();

    [Fact]
    public async Task Proposal_must_exactly_match_the_closed_tool_and_schema()
    {
        var gateway = new RecordingGateway();
        var catalog = new McpAuthorizedToolCatalog(gateway);
        var proposal = new SemanticIntentProposal(SemanticProvider.Gmail, SemanticOperation.List);

        var mismatched = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "gmail.read"),
            Invocation(GmailTools.ReadThreads, proposal));
        var extraInput = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "gmail.read"),
            new ToolInvocation(
                GmailTools.ReadMessages,
                JsonElement.Parse("""{"provider":"gmail","operation":"list","limit":1,"query":"from:anyone"}""")));

        Assert.Equal(ToolOutcomeKind.Denied, mismatched.Kind);
        Assert.Equal(ToolOutcomeKind.Denied, extraInput.Kind);
        Assert.Equal(0, gateway.ProviderCallCount);
    }

    [Fact]
    public async Task Typed_provider_reads_require_a_user_principal_and_the_provider_grant()
    {
        var gateway = new RecordingGateway();
        var catalog = new McpAuthorizedToolCatalog(gateway);

        var missingGmailGrant = await catalog.InvokeAsync(
            Context(PrincipalKind.User),
            Invocation(GmailTools.ReadMessages,
                new SemanticIntentProposal(SemanticProvider.Gmail, SemanticOperation.List)));
        var servicePrincipal = await catalog.InvokeAsync(
            Context(PrincipalKind.Service, "gmail.read"),
            Invocation(GmailTools.ReadMessages,
                new SemanticIntentProposal(SemanticProvider.Gmail, SemanticOperation.List)));
        var missingSalesforceGrant = await catalog.InvokeAsync(
            Context(PrincipalKind.User),
            Invocation(SalesforceTools.ReadRecords,
                new SemanticIntentProposal(SemanticProvider.Salesforce, SemanticOperation.List, Entity: "Account")));

        Assert.Equal(ToolOutcomeKind.Denied, missingGmailGrant.Kind);
        Assert.Equal(ToolOutcomeKind.Denied, servicePrincipal.Kind);
        Assert.Equal(ToolOutcomeKind.Denied, missingSalesforceGrant.Kind);
        Assert.Equal(0, gateway.ProviderCallCount);
    }

    [Fact]
    public async Task Gmail_metadata_request_and_grounding_are_typed_bounded_and_content_free()
    {
        var gateway = new RecordingGateway
        {
            GmailMessagesResult = new GmailMessageListResult(
                GmailReadStatus.Success,
                [new GmailMessageMetadata(
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
                new GmailResultCoverage(1, 1, 1, 1, 0, ProviderExhausted: true, CandidateLimitReached: false))
        };
        var catalog = new McpAuthorizedToolCatalog(gateway);
        var context = Context(PrincipalKind.User, "gmail.read");
        var proposal = new SemanticIntentProposal(
            SemanticProvider.Gmail,
            SemanticOperation.List,
            Entity: "Inbox",
            Limit: 2,
            Filters: [new SemanticFilter("readState", SemanticFilterOperator.Equals, "unread")]);

        var outcome = await catalog.InvokeAsync(context, Invocation(GmailTools.ReadMessages, proposal));

        Assert.Equal(ToolOutcomeKind.Success, outcome.Kind);
        Assert.Equal(RequestScope.Id(context), gateway.LastOwnerScope);
        Assert.Equal(GmailMailboxScope.Inbox, gateway.LastGmailMessagesRequest!.Selection.Mailbox);
        Assert.Equal(GmailMessageReadState.Unread, gateway.LastGmailMessagesRequest.Selection.ReadState);
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
    public async Task Gmail_mailbox_overview_is_grounded_so_provider_counts_never_enter_model_memory()
    {
        var gateway = new RecordingGateway
        {
            GmailOverviewResult = new GmailMailboxOverviewResult(
                GmailReadStatus.Success,
                InboxMessages: 12,
                UnreadInboxMessages: 3,
                InboxThreads: 9,
                UnreadInboxThreads: 2)
        };
        var catalog = new McpAuthorizedToolCatalog(gateway);
        var outcome = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "gmail.read"),
            Invocation(
                GmailTools.ReadMailboxOverview,
                new SemanticIntentProposal(SemanticProvider.Gmail, SemanticOperation.Overview)));

        Assert.Equal(ToolOutcomeKind.Success, outcome.Kind);
        Assert.NotNull(outcome.GroundingContent);
        Assert.Equal(outcome.Content!.Value.GetRawText(), outcome.GroundingContent!.Value.GetRawText());
    }

    [Fact]
    public async Task Gmail_summary_requires_the_separate_content_grant_without_calling_a_provider()
    {
        var gateway = new RecordingGateway();
        var catalog = new McpAuthorizedToolCatalog(gateway);

        var outcome = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "gmail.read"),
            Invocation(GmailTools.SummarizeThread,
                new SemanticIntentProposal(SemanticProvider.Gmail, SemanticOperation.Summarize)));

        Assert.Equal(ToolOutcomeKind.Denied, outcome.Kind);
        Assert.Contains("gmail.read.content", outcome.SafeReason, StringComparison.Ordinal);
        Assert.Equal(0, gateway.ProviderCallCount);
    }

    [Fact]
    public async Task Salesforce_read_dispatches_a_compiled_schema_aware_request_and_serializes_opaque_continuation()
    {
        const string continuation = "7c3d4268c1a4455c87f942bb7351b67e";
        var gateway = new RecordingGateway
        {
            SalesforceResult = new SalesforceReadResult(
                SalesforceReadStatus.Success,
                """{"Entity":"Opportunity","Records":[{"Entity":"Opportunity","RecordId":"006000000000001","Fields":{"Name":"Renewal"}}]}""",
                Scope: new SalesforceReadScope("user", "org", "sf-user"),
                Continuation: new SalesforceContinuation(continuation, "user", "org"),
                ReturnedCount: 1,
                TotalSize: 2)
        };
        var catalog = new McpAuthorizedToolCatalog(gateway);
        var proposal = new SemanticIntentProposal(
            SemanticProvider.Salesforce,
            SemanticOperation.List,
            Entity: "Opportunity",
            Limit: 4,
            Filters: [new SemanticFilter("open", SemanticFilterOperator.Equals, "true")],
            Sorts: [new SemanticSort("Close Date", SemanticSortDirection.Descending)]);

        var outcome = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "salesforce.read"),
            Invocation(SalesforceTools.ReadRecords, proposal));

        Assert.Equal(ToolOutcomeKind.Success, outcome.Kind);
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
            SalesforceResult = new SalesforceReadResult(SalesforceReadStatus.Success, "[]")
        };
        var grounding = new ToolGrounding(
            SalesforceTools.ReadRecords,
            JsonSerializer.SerializeToElement(new { continuation }));
        var store = new SnapshotConversationStore(new InoConversationSnapshot(
            "conversation",
            1,
            [],
            [new InoConversationOperation(
                "command",
                "list accounts",
                InoConversationStates.Succeeded,
                null,
                false,
                DateTimeOffset.UtcNow,
                Groundings: [grounding])]));
        var catalog = new McpAuthorizedToolCatalog(gateway, store);

        var outcome = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "salesforce.read"),
            Invocation(SalesforceTools.ContinueRecords,
                new SemanticIntentProposal(SemanticProvider.Salesforce, SemanticOperation.NextPage)));

        Assert.Equal(ToolOutcomeKind.Success, outcome.Kind);
        Assert.Equal(continuation, gateway.LastSalesforceContinuationRequest!.Value);
        Assert.Equal(1, gateway.ProviderCallCount);
    }

    [Fact]
    public async Task Salesforce_mutation_preview_is_local_and_never_calls_a_provider()
    {
        var gateway = new RecordingGateway();
        var catalog = new McpAuthorizedToolCatalog(gateway);
        var proposal = new SemanticIntentProposal(
            SemanticProvider.Salesforce,
            SemanticOperation.MutationPreview,
            Entity: "Account",
            SearchText: "Acme",
            Filters: [new SemanticFilter("Industry", SemanticFilterOperator.Set, "Technology")]);

        var denied = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "salesforce.read"),
            Invocation(SalesforceTools.PreviewMutation, proposal));
        var outcome = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "salesforce.read", "salesforce.mutation.preview"),
            Invocation(SalesforceTools.PreviewMutation, proposal));

        Assert.Equal(ToolOutcomeKind.Denied, denied.Kind);
        Assert.Equal(ToolOutcomeKind.Success, outcome.Kind);
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
            GmailMessagesResult = new GmailMessageListResult(
                GmailReadStatus.Success,
                [new GmailMessageMetadata(
                    "message-1", "thread-1", 3_000, "One <one@example.com>", "one@example.com",
                    null, [], "One", [], true)],
                new GmailResultCoverage(1, 3, 3, 3, 0, true, false),
                StableCandidateMessageIds: ["message-1", "message-2", "message-3"])
        };
        var context = Context(PrincipalKind.User, "gmail.read");
        var first = await new McpAuthorizedToolCatalog(firstGateway).InvokeAsync(
            context,
            Invocation(GmailTools.ReadMessages,
                new SemanticIntentProposal(SemanticProvider.Gmail, SemanticOperation.List)));
        var store = Store(new ToolGrounding(GmailTools.ReadMessages, first.GroundingContent!.Value));
        var nextGateway = new RecordingGateway();

        await new McpAuthorizedToolCatalog(nextGateway, store).InvokeAsync(
            context,
            Invocation(GmailTools.ReadMessages, new SemanticIntentProposal(
                SemanticProvider.Gmail,
                SemanticOperation.Previous,
                Ordinal: 1,
                Reference: SemanticReference.LatestProviderResult)));

        Assert.Equal(1, nextGateway.LastGmailMessagesRequest!.Offset);
        Assert.Equal(["message-1", "message-2", "message-3"],
            nextGateway.LastGmailMessagesRequest.Selection.PinnedMessageIds!);
    }

    [Fact]
    public async Task Gmail_ordinal_followup_reads_one_stable_result_at_the_requested_position()
    {
        var context = Context(PrincipalKind.User, "gmail.read");
        var selection = new GmailMessageSelection(
            PinnedMessageIds: ["message-1", "message-2"],
            MaxCandidates: 2);
        var store = Store(new ToolGrounding(
            GmailTools.ReadMessages,
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

        var outcome = await new McpAuthorizedToolCatalog(gateway, store).InvokeAsync(
            context,
            Invocation(GmailTools.ReadMessages, new SemanticIntentProposal(
                SemanticProvider.Gmail,
                SemanticOperation.List,
                Limit: 2,
                Ordinal: 2,
                Reference: SemanticReference.LatestProviderResult)));

        Assert.Equal(ToolOutcomeKind.Success, outcome.Kind);
        Assert.Equal(1, gateway.LastGmailMessagesRequest!.Offset);
        Assert.Equal(1, gateway.LastGmailMessagesRequest.Limit);
        Assert.Equal(["message-1", "message-2"],
            gateway.LastGmailMessagesRequest.Selection.PinnedMessageIds!);
    }

    [Fact]
    public async Task Gmail_pinned_page_advances_over_consumed_ids_when_refetched_candidates_are_unavailable()
    {
        var context = Context(PrincipalKind.User, "gmail.read");
        var selection = new GmailMessageSelection(
            PinnedMessageIds: ["message-1", "message-2", "message-3"],
            MaxCandidates: 3);
        var store = Store(new ToolGrounding(
            GmailTools.ReadMessages,
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
            GmailMessagesResult = new GmailMessageListResult(
                GmailReadStatus.Success,
                [],
                new GmailResultCoverage(0, 2, 0, 0, 2, true, false),
                StableCandidateMessageIds: ["message-1", "message-2", "message-3"])
        };

        var outcome = await new McpAuthorizedToolCatalog(gateway, store).InvokeAsync(
            context,
            Invocation(GmailTools.ReadMessages, new SemanticIntentProposal(
                SemanticProvider.Gmail,
                SemanticOperation.Previous,
                Limit: 2,
                Reference: SemanticReference.LatestProviderResult)));

        Assert.Equal(ToolOutcomeKind.Success, outcome.Kind);
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
            SalesforceResult = new SalesforceReadResult(
                SalesforceReadStatus.Success,
                """{"Entity":"Opportunity","Records":[{"Entity":"Opportunity","RecordId":"006000000000001","Fields":{"Name":"Renewal"}}]}""",
                ReturnedCount: 1)
        };
        var context = Context(PrincipalKind.User, "salesforce.read");
        var first = await new McpAuthorizedToolCatalog(firstGateway).InvokeAsync(
            context,
            Invocation(SalesforceTools.ReadRecords, new SemanticIntentProposal(
                SemanticProvider.Salesforce,
                SemanticOperation.List,
                Entity: "Opportunity")));
        var store = Store(new ToolGrounding(SalesforceTools.ReadRecords, first.GroundingContent!.Value));
        var relatedGateway = new RecordingGateway();

        await new McpAuthorizedToolCatalog(relatedGateway, store).InvokeAsync(
            context,
            Invocation(SalesforceTools.ReadRecords, new SemanticIntentProposal(
                SemanticProvider.Salesforce,
                SemanticOperation.Related,
                Entity: "Contact",
                Reference: SemanticReference.LatestProviderResult)));

        Assert.Equal("006000000000001", relatedGateway.LastSalesforceReadRequest!.RelatedTo!.RecordId);
        Assert.Equal("Opportunity", relatedGateway.LastSalesforceReadRequest.RelatedTo.Entity.Label);
    }

    [Theory]
    [InlineData(SemanticOperation.Details)]
    [InlineData(SemanticOperation.Related)]
    public async Task Salesforce_followup_requires_one_grounded_record_or_an_explicit_ordinal(
        SemanticOperation operation)
    {
        var context = Context(PrincipalKind.User, "salesforce.read");
        var store = Store(new ToolGrounding(
            SalesforceTools.ReadRecords,
            JsonSerializer.SerializeToElement(new
            {
                entity = "Account",
                recordIds = new[] { "001000000000001", "001000000000002" },
                resultCount = 2
            }, SemanticJson)));
        var ambiguousGateway = new RecordingGateway();
        var entity = operation == SemanticOperation.Related ? "Contact" : "Account";

        var ambiguous = await new McpAuthorizedToolCatalog(ambiguousGateway, store).InvokeAsync(
            context,
            Invocation(SalesforceTools.ReadRecords, new SemanticIntentProposal(
                SemanticProvider.Salesforce,
                operation,
                Entity: entity,
                Reference: SemanticReference.LatestProviderResult)));

        Assert.Equal(ToolOutcomeKind.PermanentFailure, ambiguous.Kind);
        Assert.Contains("multiple records", ambiguous.SafeReason, StringComparison.Ordinal);
        Assert.Equal(0, ambiguousGateway.ProviderCallCount);

        var ordinalGateway = new RecordingGateway();
        var selected = await new McpAuthorizedToolCatalog(ordinalGateway, store).InvokeAsync(
            context,
            Invocation(SalesforceTools.ReadRecords, new SemanticIntentProposal(
                SemanticProvider.Salesforce,
                operation,
                Entity: entity,
                Ordinal: 2,
                Reference: SemanticReference.LatestProviderResult)));

        Assert.Equal(ToolOutcomeKind.Success, selected.Kind);
        var resolved = operation == SemanticOperation.Related
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
            GmailMessagesResult = new GmailMessageListResult(
                GmailReadStatus.Success,
                [new GmailMessageMetadata(
                    "message-1", "thread-1", 1_700_000_000_000,
                    "Ada <ada@example.com>", "ada@example.com", null, [], null, [], true)],
                new GmailResultCoverage(1, 1, 1, 1, 0, true, false)),
            SalesforceResult = new SalesforceReadResult(
                SalesforceReadStatus.Success,
                """[{"Id":"001-a"},{"Id":"001-b"}]""",
                ReturnedCount: 2)
        };
        var catalog = new McpAuthorizedToolCatalog(gateway);
        var proposal = new SemanticIntentProposal(
            SemanticProvider.CrossProvider,
            SemanticOperation.Match,
            Entity: "Account",
            Limit: 3,
            Reference: SemanticReference.LatestGmailSender);

        var outcome = await catalog.InvokeAsync(
            Context(PrincipalKind.User, "gmail.read", "salesforce.read"),
            Invocation(CrossProviderTools.MatchSalesforceAccountToGmailSender, proposal));

        Assert.Equal(ToolOutcomeKind.PermanentFailure, outcome.Kind);
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

        var text = await new McpResponseComposer().ComposeAsync(
            Context(PrincipalKind.User, "salesforce.read"),
            new ModelResponse(string.Empty, "deterministic-tool-response", true),
            [new ToolOutcome(ToolOutcomeKind.Success, content)]);

        Assert.DoesNotContain("evil.example", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[external address omitted]", text, StringComparison.Ordinal);
    }

    private static ToolInvocation Invocation(string toolId, SemanticIntentProposal proposal) =>
        new(toolId, JsonSerializer.SerializeToElement(proposal, SemanticJson));

    private static RuntimeRequestContext Context(PrincipalKind principalKind, params string[] grants) => new(
        new TenantId("tenant"),
        new WorkspaceId("workspace"),
        new PrincipalRef("principal", principalKind),
        "session",
        AuthAssurance.Password,
        "correlation",
        "idempotency",
        grants.ToHashSet(StringComparer.Ordinal));

    private static SnapshotConversationStore Store(ToolGrounding grounding) => new(new InoConversationSnapshot(
        "conversation",
        1,
        [],
        [new InoConversationOperation(
            "command",
            "provider read",
            InoConversationStates.Succeeded,
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

    private sealed class RecordingGateway : IMcpIntegrationToolGateway
    {
        private static readonly GmailResultCoverage EmptyCoverage = new(0, 0, 0, 0, 0, true, false);

        public int ProviderCallCount { get; private set; }
        public string? LastOwnerScope { get; private set; }
        public GmailMessageListRequest? LastGmailMessagesRequest { get; private set; }
        public SalesforceRecordReadRequest? LastSalesforceReadRequest { get; private set; }
        public SalesforceSearchRequest? LastSalesforceSearchRequest { get; private set; }
        public SalesforceContinuationRequest? LastSalesforceContinuationRequest { get; private set; }
        public GmailMessageListResult GmailMessagesResult { get; init; } =
            new(GmailReadStatus.Success, [], EmptyCoverage);
        public GmailMailboxOverviewResult GmailOverviewResult { get; init; } =
            new(GmailReadStatus.Success);
        public SalesforceReadResult SalesforceResult { get; init; } =
            new(SalesforceReadStatus.Success, "[]");

        public Task<GmailReadResult> ReadIncomingAtOffsetAsync(
            string ownerScope,
            GmailReadRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            return Task.FromResult(new GmailReadResult(GmailReadStatus.Unavailable));
        }

        public Task<SalesforceReadResult> ReadSalesforceAsync(
            string ownerScope,
            string toolId,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            return Task.FromResult(SalesforceResult);
        }

        public Task<GmailMessageListResult> ReadGmailMessagesAsync(
            string ownerScope,
            GmailMessageListRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            LastGmailMessagesRequest = request;
            return Task.FromResult(GmailMessagesResult);
        }

        public Task<GmailMailboxOverviewResult> ReadGmailMailboxOverviewAsync(
            string ownerScope,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            return Task.FromResult(GmailOverviewResult);
        }

        public Task<GmailThreadListResult> ReadGmailThreadsAsync(
            string ownerScope,
            GmailThreadListRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            return Task.FromResult(new GmailThreadListResult(GmailReadStatus.Success, [], EmptyCoverage));
        }

        public Task<SalesforceReadResult> DiscoverSalesforceObjectsAsync(
            string ownerScope,
            SalesforceDiscoveryRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            return Task.FromResult(SalesforceResult);
        }

        public Task<SalesforceReadResult> ReadSalesforceRecordsAsync(
            string ownerScope,
            SalesforceRecordReadRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            LastSalesforceReadRequest = request;
            return Task.FromResult(SalesforceResult);
        }

        public Task<SalesforceReadResult> SearchSalesforceRecordsAsync(
            string ownerScope,
            SalesforceSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            LastSalesforceSearchRequest = request;
            return Task.FromResult(SalesforceResult);
        }

        public Task<SalesforceReadResult> AggregateSalesforceRecordsAsync(
            string ownerScope,
            SalesforceAggregateRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(ownerScope);
            return Task.FromResult(SalesforceResult);
        }

        public Task<SalesforceReadResult> ContinueSalesforceRecordsAsync(
            string ownerScope,
            SalesforceContinuationRequest request,
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

    private sealed class SnapshotConversationStore(InoConversationSnapshot snapshot) : IInoConversationStore
    {
        public Task<InoConversationSnapshot> ReadAsync(
            RuntimeRequestContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(snapshot);

        public Task<InoConversationSnapshot> BeginAsync(
            RuntimeRequestContext context,
            string commandId,
            string prompt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InoConversationSnapshot> TransitionAsync(
            RuntimeRequestContext context,
            string commandId,
            string state,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InoConversationSnapshot> CompleteAsync(
            RuntimeRequestContext context,
            string commandId,
            string response,
            ToolAction? action = null,
            ToolGrounding? grounding = null,
            IReadOnlyList<ToolGrounding>? groundings = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InoConversationSnapshot> AwaitAuthorizationAsync(
            RuntimeRequestContext context,
            string commandId,
            string response,
            ToolAction action,
            ExternalAuthorizationContinuation authorization,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InoConversationSnapshot> FailAsync(
            RuntimeRequestContext context,
            string commandId,
            string safeReason,
            bool retryable,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InoConversationSnapshot> RecordOutcomeUnknownAsync(
            RuntimeRequestContext context,
            string commandId,
            string safeReason,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
