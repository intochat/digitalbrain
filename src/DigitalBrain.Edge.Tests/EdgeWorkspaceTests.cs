using System.Text.Json;
using DigitalBrain.Product.Approvals;
using DigitalBrain.Product.Presentation;
using DigitalBrain.Product.SalesInsights;

namespace DigitalBrain.Edge.Tests;

public sealed class EdgeWorkspaceTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly NeuronId ApprovalProjection = new(
        ApprovalWorkspaceProjectionNeuron.Kind,
        ApprovalWorkspaceInboxNeuron.Name);

    public static IEnumerable<object?[]> SafeUnavailableReasons
        => Enum.GetValues<SalesInsightUnavailableReason>()
            .Select(static reason => new object?[] { reason });

    [Fact]
    public async Task ReplayedChatApprovalBuildsChatDrawerInboxAndPendingOpaqueActions()
    {
        var approval = ApprovalSurface(
            revision: 7,
            Item(
                proposalId: "approval-chat",
                fingerprint: "fingerprint-chat",
                context: new ApprovalReviewContext(
                    ApprovalReviewContextKind.ChatConversation,
                    "conversation/acme"),
                status: ApprovalWorkspaceItemStatus.Pending,
                approveReference: "apr_chat_approve",
                rejectReference: "apr_chat_reject"));
        var source = SourceWithApproval(approval);

        var snapshot = await new WorkspaceUiAssembler(source).ReadAsync([], CancellationToken.None);

        Assert.Equal(7, snapshot.Revision);
        var surface = Assert.Single(snapshot.Surfaces);
        Assert.Equal("approvals", surface.SurfaceId);
        Assert.Contains(surface.Components, static component => component is ChatComponent chat
            && chat.Route == "conversation/acme");
        Assert.Contains(surface.Components, static component => component is DrawerComponent drawer
            && drawer.Route == "conversation/acme");
        Assert.Contains(surface.Components, static component => component is InboxComponent);
        Assert.Contains(surface.Components, static component => component is CardComponent card
            && card.Title == "Review approval-chat");
        Assert.Contains(surface.Components, static component => component is StatusComponent status
            && status.Value == "Pending");
        Assert.Contains(surface.Components, static component => component is EvidenceComponent evidence
            && Assert.Single(evidence.Items).Source == "gmail");
        Assert.Contains(surface.Components, static component => component is ChangesComponent changes
            && Assert.Single(changes.Items).Field == "Description");
        var actions = surface.Components.OfType<ActionComponent>().ToArray();
        Assert.Equal(["apr_chat_approve", "apr_chat_reject"], actions.Select(static action => action.Action.Value));

        var serialized = JsonSerializer.Serialize<IReadOnlyList<BaseUiKitComponent>>(surface.Components, Json);
        var roundTripped = JsonSerializer.Deserialize<IReadOnlyList<BaseUiKitComponent>>(serialized, Json);
        Assert.NotNull(roundTripped);
        Assert.Contains(roundTripped, static component => component is ChatComponent);
        Assert.Contains(roundTripped, static component => component is ActionComponent);
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<IReadOnlyList<BaseUiKitComponent>>(
            "[{\"component\":\"Unknown\"}]",
            Json));
    }

    [Fact]
    public async Task WebhookApprovalBuildsInboxOnlyAndTerminalApprovalsHaveNoActions()
    {
        var approval = ApprovalSurface(
            revision: 4,
            Item(
                proposalId: "approval-webhook",
                fingerprint: "fingerprint-webhook",
                context: null,
                status: ApprovalWorkspaceItemStatus.Approved,
                approveReference: "apr_webhook_approve",
                rejectReference: "apr_webhook_reject"));

        var snapshot = await new WorkspaceUiAssembler(SourceWithApproval(approval)).ReadAsync([], CancellationToken.None);

        var surface = Assert.Single(snapshot.Surfaces);
        Assert.Contains(surface.Components, static component => component is InboxComponent);
        Assert.DoesNotContain(surface.Components, static component => component is ChatComponent);
        Assert.DoesNotContain(surface.Components, static component => component is DrawerComponent);
        Assert.Contains(surface.Components, static component => component is StatusComponent status
            && status.Value == "Approved");
        Assert.Empty(surface.Components.OfType<ActionComponent>());
    }

    [Theory]
    [InlineData(ApprovalWorkspaceItemStatus.Rejected, "Rejected")]
    [InlineData(ApprovalWorkspaceItemStatus.Expired, "Expired")]
    [InlineData(ApprovalWorkspaceItemStatus.MutationUncertain, "MutationUncertain")]
    public async Task TerminalApprovalStatusesRenderStatusWithoutActions(
        ApprovalWorkspaceItemStatus status,
        string expectedStatus)
    {
        var approval = ApprovalSurface(
            revision: 4,
            Item(
                proposalId: $"approval-{expectedStatus}",
                fingerprint: $"fingerprint-{expectedStatus}",
                context: null,
                status: status,
                approveReference: $"apr_{expectedStatus}_approve",
                rejectReference: $"apr_{expectedStatus}_reject"));

        var snapshot = await new WorkspaceUiAssembler(SourceWithApproval(approval)).ReadAsync([], CancellationToken.None);

        var surface = Assert.Single(snapshot.Surfaces);
        Assert.Contains(surface.Components, component => component is StatusComponent rendered
            && rendered.Value == expectedStatus);
        Assert.Empty(surface.Components.OfType<ActionComponent>());
    }

    [Fact]
    public async Task SalesReadyBuildsBarChartAndTableWhileUnavailableBuildsOnlyUnavailable()
    {
        var readyId = "sales-ready";
        var unavailableId = "sales-unavailable";
        var reader = new FakeJournalReader();
        reader.Add(
            new NeuronId(SalesInsightProjectionNeuron.Kind, readyId),
            Produced(11, SalesSurface(readyId)));
        reader.Add(
            new NeuronId(SalesInsightProjectionNeuron.Kind, unavailableId),
            Produced(13, SalesUnavailable(unavailableId)));
        var source = new WorkspaceUiSurfaceSource(new FakeWorkspaceChannel(reader));

        var ready = await source.ReadSalesAsync(readyId, CancellationToken.None);
        var unavailable = await source.ReadSalesAsync(unavailableId, CancellationToken.None);

        Assert.NotNull(ready);
        Assert.Equal(11, ready.Revision);
        Assert.Contains(ready.Components, static component => component is BarChartComponent);
        Assert.Contains(ready.Components, static component => component is TableComponent);
        Assert.NotNull(unavailable);
        Assert.Equal(13, unavailable.Revision);
        var unavailableComponent = Assert.Single(unavailable.Components);
        var typedUnavailable = Assert.IsType<UnavailableComponent>(unavailableComponent);
        Assert.Equal("ReaderUnavailable", typedUnavailable.Reason);
    }

    [Theory]
    [MemberData(nameof(SafeUnavailableReasons))]
    public async Task EveryDefinedUnavailableReasonBuildsExactlyOneSafeUnavailableComponent(
        SalesInsightUnavailableReason reason)
    {
        var queryId = $"sales-unavailable-{(int)reason}";
        var reader = new FakeJournalReader();
        reader.Add(
            new NeuronId(SalesInsightProjectionNeuron.Kind, queryId),
            Produced(17, SalesUnavailable(queryId, reason)));

        var surface = await new WorkspaceUiSurfaceSource(new FakeWorkspaceChannel(reader))
            .ReadSalesAsync(queryId, CancellationToken.None);

        Assert.NotNull(surface);
        var component = Assert.IsType<UnavailableComponent>(Assert.Single(surface.Components));
        Assert.False(string.IsNullOrWhiteSpace(component.Reason));
    }

    [Fact]
    public async Task UndefinedUnavailableReasonBuildsAGenericSafeUnavailableComponent()
    {
        const string queryId = "sales-unavailable-generic";
        var reader = new FakeJournalReader();
        reader.Add(
            new NeuronId(SalesInsightProjectionNeuron.Kind, queryId),
            Produced(17, SalesUnavailable(queryId, (SalesInsightUnavailableReason)999)));

        var surface = await new WorkspaceUiSurfaceSource(new FakeWorkspaceChannel(reader))
            .ReadSalesAsync(queryId, CancellationToken.None);

        Assert.NotNull(surface);
        var component = Assert.IsType<UnavailableComponent>(Assert.Single(surface.Components));
        Assert.Equal("Unavailable", component.Reason);
    }

    [Fact]
    public async Task ReadsEveryJournalPageAndReturnsNoSurfaceForAbsentOrUnavailableHistory()
    {
        var latest = ApprovalSurface(
            revision: 9,
            Item(
                proposalId: "approval-latest",
                fingerprint: "fingerprint-latest",
                context: null,
                status: ApprovalWorkspaceItemStatus.Pending,
                approveReference: "apr_latest_approve",
                rejectReference: "apr_latest_reject"));
        var reader = new FakeJournalReader();
        reader.Add(
            ApprovalProjection,
            Produced(1, new IgnoredPresentationFact("not-an-approval-surface")),
            Received(2, ApprovalSurface(
                revision: 1,
                Item(
                    proposalId: "received-only",
                    fingerprint: "fingerprint-received",
                    context: null,
                    status: ApprovalWorkspaceItemStatus.Pending,
                    approveReference: "apr_received_approve",
                    rejectReference: "apr_received_reject"))),
            Produced(3, ApprovalSurface(
                revision: 2,
                Item(
                    proposalId: "approval-old",
                    fingerprint: "fingerprint-old",
                    context: null,
                    status: ApprovalWorkspaceItemStatus.Pending,
                    approveReference: "apr_old_approve",
                    rejectReference: "apr_old_reject"))),
            Produced(4, latest));
        var source = new WorkspaceUiSurfaceSource(new FakeWorkspaceChannel(reader), journalPageSize: 2);

        var approvals = await source.ReadApprovalsAsync(CancellationToken.None);
        var readsAfterApprovals = reader.ReadCount;
        var absent = await source.ReadSalesAsync("missing-sales", CancellationToken.None);
        reader.HistoryUnavailable = true;
        var unavailable = await source.ReadApprovalsAsync(CancellationToken.None);

        Assert.NotNull(approvals);
        Assert.Equal(9, approvals.Revision);
        Assert.Equal(2, readsAfterApprovals);
        Assert.Null(absent);
        Assert.Null(unavailable);
    }

    [Fact]
    public async Task SerializedUiContainsNoBindingsProviderSecretsOrWorkspaceScope()
    {
        var approval = ApprovalSurface(
            revision: 3,
            Item(
                proposalId: "approval-safe",
                fingerprint: "fingerprint-safe",
                context: new ApprovalReviewContext(
                    ApprovalReviewContextKind.ChatConversation,
                    "conversation/safe"),
                status: ApprovalWorkspaceItemStatus.Pending,
                approveReference: "apr_safe_approve",
                rejectReference: "apr_safe_reject"));
        var snapshot = await new WorkspaceUiAssembler(SourceWithApproval(approval)).ReadAsync([], CancellationToken.None);

        var serialized = JsonSerializer.Serialize(snapshot, Json);

        foreach (var forbidden in new[]
                 {
                     "actionBinding", "executionTarget", "salesforce", "mutation", "credential", "token",
                     "scope", "workspace", "soql", "provider-secret", "fingerprint-safe",
                 })
        {
            Assert.DoesNotContain(forbidden, serialized, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task MalformedUnknownNonPendingAndOtherChannelActionsDoNotAuthorizeOrPublish()
    {
        var current = ApprovalSurface(
            revision: 3,
            Item(
                proposalId: "approval-current",
                fingerprint: "fingerprint-current",
                context: null,
                status: ApprovalWorkspaceItemStatus.Pending,
                approveReference: "apr_current_approve",
                rejectReference: "apr_current_reject"),
            Item(
                proposalId: "approval-terminal",
                fingerprint: "fingerprint-terminal",
                context: null,
                status: ApprovalWorkspaceItemStatus.Expired,
                approveReference: "apr_terminal_approve",
                rejectReference: "apr_terminal_reject"));
        var authorizer = new FakeAuthorizer();
        var publisher = new FakePublisher();
        var (bridge, reader) = BridgeWithApproval(current, publisher, authorizer);
        var otherWorkspace = ApprovalSurface(
            revision: 5,
            Item(
                proposalId: "approval-other-workspace",
                fingerprint: "fingerprint-other-workspace",
                context: null,
                status: ApprovalWorkspaceItemStatus.Pending,
                approveReference: "apr_other_workspace_approve",
                rejectReference: "apr_other_workspace_reject"));
        var otherReader = new FakeJournalReader();
        otherReader.Add(ApprovalProjection, Produced(1, otherWorkspace));
        var otherSnapshot = await new WorkspaceUiSurfaceSource(new FakeWorkspaceChannel(otherReader))
            .ReadApprovalsAsync(CancellationToken.None);
        var otherAction = Assert.Single(otherSnapshot!.Items).Actions[0].Reference;

        var malformed = await bridge.InvokeAsync(new OpaqueUiActionReference(" "), CancellationToken.None);
        var padded = await bridge.InvokeAsync(new OpaqueUiActionReference(" apr_current_approve "), CancellationToken.None);
        var unknown = await bridge.InvokeAsync(new OpaqueUiActionReference("apr_unknown"), CancellationToken.None);
        var otherChannel = await bridge.InvokeAsync(new OpaqueUiActionReference(otherAction), CancellationToken.None);
        var nonPending = await bridge.InvokeAsync(new OpaqueUiActionReference("apr_terminal_approve"), CancellationToken.None);

        Assert.False(malformed.Accepted);
        Assert.False(padded.Accepted);
        Assert.False(unknown.Accepted);
        Assert.False(otherChannel.Accepted);
        Assert.False(nonPending.Accepted);
        Assert.Equal(1, otherReader.ReadCount);
        Assert.Equal(4, reader.ReadCount);
        Assert.Equal(0, authorizer.Calls);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task DeniedValidActionAuthorizesButDoesNotPublish()
    {
        var approval = ApprovalSurface(
            revision: 3,
            Item(
                proposalId: "approval-denied",
                fingerprint: "fingerprint-denied",
                context: null,
                status: ApprovalWorkspaceItemStatus.Pending,
                approveReference: "apr_denied_approve",
                rejectReference: "apr_denied_reject"));
        var authorizer = new FakeAuthorizer { Allowed = false };
        var publisher = new FakePublisher();
        var (bridge, reader) = BridgeWithApproval(approval, publisher, authorizer);

        var receipt = await bridge.InvokeAsync(new OpaqueUiActionReference("apr_denied_approve"), CancellationToken.None);

        Assert.False(receipt.Accepted);
        Assert.Equal(1, reader.ReadCount);
        Assert.Equal(1, authorizer.Calls);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task ValidActionPublishesOnlyApprovalDecisionWithStableDecisionIdentity()
    {
        var approval = ApprovalSurface(
            revision: 3,
            Item(
                proposalId: "approval-valid",
                fingerprint: "fingerprint-valid",
                context: null,
                status: ApprovalWorkspaceItemStatus.Pending,
                approveReference: "apr_valid_approve",
                rejectReference: "apr_valid_reject"));
        var authorizer = new FakeAuthorizer();
        var publisher = new FakePublisher();
        var (bridge, reader) = BridgeWithApproval(approval, publisher, authorizer);
        var action = new OpaqueUiActionReference("apr_valid_approve");

        var first = await bridge.InvokeAsync(action, CancellationToken.None);
        var second = await bridge.InvokeAsync(action, CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.Equal(2, reader.ReadCount);
        Assert.Equal(2, authorizer.Calls);
        var published = publisher.Published
            .Select(static synapse => Assert.IsType<ApprovalDecisionSubmitted>(synapse))
            .ToArray();
        Assert.Equal(2, published.Length);
        Assert.All(published, decision =>
        {
            Assert.Equal("approval-valid", decision.ProposalId);
            Assert.Equal("fingerprint-valid", decision.ExpectedProposalFingerprint);
            Assert.Equal(ApprovalDecision.Approve, decision.Decision);
        });
        Assert.Equal(published[0].DecisionId, published[1].DecisionId);
    }

    private static FakeSurfaceSource SourceWithApproval(ApprovalWorkspaceSurfaceRequested approval)
        => new(approval);

    private static (ApprovalUiActionBridge Bridge, FakeJournalReader Reader) BridgeWithApproval(
        ApprovalWorkspaceSurfaceRequested approval,
        FakePublisher publisher,
        FakeAuthorizer authorizer)
    {
        var reader = new FakeJournalReader();
        reader.Add(ApprovalProjection, Produced(1, approval));
        return (new ApprovalUiActionBridge(new FakeWorkspaceChannel(reader, publisher), authorizer), reader);
    }

    private static ApprovalWorkspaceSurfaceRequested ApprovalSurface(
        long revision,
        params ApprovalWorkspaceSurfaceItem[] items)
        => new(revision, items);

    private static ApprovalWorkspaceSurfaceItem Item(
        string proposalId,
        string fingerprint,
        ApprovalReviewContext? context,
        ApprovalWorkspaceItemStatus status,
        string approveReference,
        string rejectReference)
        => new(
            proposalId,
            fingerprint,
            $"Review {proposalId}",
            "Apply the frozen safe update.",
            [new ApprovalEvidence("gmail", "Acme announced a verified change.", new Uri("https://evidence.example.test/acme"))],
            [new ApprovalChange("Description", "Old value", "New value")],
            new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero),
            context,
            status,
            context is null
                ? [ApprovalReviewPlacement.Inbox]
                : [ApprovalReviewPlacement.Chat, ApprovalReviewPlacement.ContextDrawer, ApprovalReviewPlacement.Inbox],
            [
                new ApprovalWorkspaceSurfaceAction(ApprovalReviewDecision.Approve, approveReference),
                new ApprovalWorkspaceSurfaceAction(ApprovalReviewDecision.Reject, rejectReference),
            ]);

    private static SalesInsightSurfaceRequested SalesSurface(string queryId)
        => new(
            queryId,
            new SalesDateRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 8)),
            "USD",
            [
                new SalesRevenueBucket(new DateOnly(2026, 8, 1), 120m, 1),
                new SalesRevenueBucket(new DateOnly(2026, 8, 2), 240m, 2),
            ],
            360m,
            3,
            new SalesInsightContext(SalesInsightContextKind.ChatConversation, "conversation/sales"),
            [SalesInsightDisplay.BarChart, SalesInsightDisplay.Table],
            [SalesInsightPlacement.Chat, SalesInsightPlacement.ContextDrawer]);

    private static SalesInsightUnavailableSurfaceRequested SalesUnavailable(
        string queryId,
        SalesInsightUnavailableReason reason = SalesInsightUnavailableReason.ReaderUnavailable)
        => new(
            queryId,
            new SalesInsightContext(SalesInsightContextKind.ChatConversation, "conversation/sales"),
            reason,
            [SalesInsightPlacement.Chat, SalesInsightPlacement.ContextDrawer]);

    private static JournalRecord Produced<TSynapse>(long position, TSynapse synapse)
        where TSynapse : Synapse
        => Record(position, JournalRecordDirection.Produced, synapse);

    private static JournalRecord Received<TSynapse>(long position, TSynapse synapse)
        where TSynapse : Synapse
        => Record(position, JournalRecordDirection.Received, synapse);

    private static JournalRecord Record<TSynapse>(
        long position,
        JournalRecordDirection direction,
        TSynapse synapse)
        where TSynapse : Synapse
        => new(
            position,
            direction,
            typeof(TSynapse).FullName!,
            new SynapseOrigin(new NeuronId("test", "source"), position, DateTimeOffset.UnixEpoch),
            null,
            [],
            JsonSerializer.SerializeToElement(synapse, Json));

    private sealed record IgnoredPresentationFact(string Value) : Synapse;

    private sealed class FakeSurfaceSource(ApprovalWorkspaceSurfaceRequested? approval) : IWorkspaceUiSurfaceSource
    {
        public Task<ApprovalWorkspaceSurfaceRequested?> ReadApprovalsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(approval);
        }

        public Task<UiSurface?> ReadSalesAsync(string queryId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<UiSurface?>(null);
        }
    }

    private sealed class FakeWorkspaceChannel(FakeJournalReader reader, FakePublisher? publisher = null) : WorkspaceChannel
    {
        public SynapsePublisher Publisher { get; } = publisher ?? new FakePublisher();

        public JournalReader Journal { get; } = reader;
    }

    private sealed class FakeJournalReader : JournalReader
    {
        private readonly Dictionary<NeuronId, List<JournalRecord>> records = [];

        public bool HistoryUnavailable { get; set; }

        public int ReadCount { get; private set; }

        public void Add(NeuronId neuron, params JournalRecord[] values)
        {
            if (!records.TryGetValue(neuron, out var journal))
            {
                journal = [];
                records.Add(neuron, journal);
            }

            journal.AddRange(values);
            journal.Sort(static (left, right) => left.Position.CompareTo(right.Position));
        }

        public Task<JournalRead> ReadAsync(
            NeuronId neuron,
            long afterPosition,
            int maximumRecords,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            var journal = records.TryGetValue(neuron, out var values) ? values : [];
            var end = journal.Count == 0 ? 0 : journal[^1].Position;
            if (HistoryUnavailable)
            {
                return Task.FromResult<JournalRead>(new JournalHistoryUnavailable(afterPosition, 1, end));
            }

            var page = journal.Where(record => record.Position > afterPosition)
                .Take(maximumRecords)
                .ToArray();
            var readThrough = page.Length == 0 ? end : page[^1].Position;
            return Task.FromResult<JournalRead>(new JournalPage(page, readThrough, end));
        }
    }

    private sealed class FakePublisher : SynapsePublisher
    {
        public List<Synapse> Published { get; } = [];

        public Task PublishAsync(Synapse synapse, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(synapse);
            cancellationToken.ThrowIfCancellationRequested();
            Published.Add(synapse);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthorizer : IUiActionAuthorizer
    {
        public bool Allowed { get; set; } = true;

        public int Calls { get; private set; }

        public Task<bool> AuthorizeAsync(OpaqueUiActionReference action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Allowed);
        }
    }
}
