extern alias McpProject;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Core.V2;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;
using DigitalBrain.Kernel.Runtime;
using Orleans;
using McpEffectCommandHandler = McpProject::DigitalBrain.Mcp.McpEffectCommandHandler;
using McpInoCommandHandler = McpProject::DigitalBrain.Mcp.McpInoCommandHandler;
using InoEffectStore = McpProject::DigitalBrain.Mcp.InoEffectStore;
using McpConversationContextAssembler = McpProject::DigitalBrain.Mcp.McpConversationContextAssembler;
using McpNoToolCatalog = McpProject::DigitalBrain.Mcp.McpNoToolCatalog;
using McpNoToolPlanner = McpProject::DigitalBrain.Mcp.McpNoToolPlanner;
using McpResponseComposer = McpProject::DigitalBrain.Mcp.McpResponseComposer;

namespace DigitalBrain.Tests.Runtime;

public sealed class ContractsTests
{
    [Fact]
    public async Task ino_command_is_identity_free_durable_and_projects_a_principal_conversation()
    {
        var context = new RuntimeRequestContext(new("tenant-a"), new("workspace-a"), new("user-a", PrincipalKind.User), "session", AuthAssurance.Password, "corr", "same-retry", new HashSet<string> { "brain.act" });
        var other = context with { WorkspaceId = new("workspace-b"), Principal = new("user-b", PrincipalKind.User) };
        var feed = new PrivateFeedStore(); var effects = new InoEffectStore();
        var surfaces = new WorkspaceSurfaceProducer(feed, new ActionExecutor(feed), effects);
        var owner = new ConversationOwner(new McpConversationContextAssembler(effects), new McpNoToolPlanner(),
            new FakeModelRouter(), new McpNoToolCatalog(), new McpResponseComposer());
        var handler = new McpInoCommandHandler(effects, surfaces, owner);
        var result = await handler.ExecuteAsync(new CommandEnvelope("ino.interact", 2, "ino-command", context, JsonSerializer.SerializeToElement(new { prompt = "Summarize my workspace" })));
        Assert.Equal(WorkflowState.Succeeded, result.State);
        Assert.Equal(2, effects.Read(context).Turns.Count);
        Assert.Equal(4, feed.CatchUp(context, SurfaceAudienceKind.Principal, 0).Items.Count);
        Assert.Empty(feed.CatchUp(context, SurfaceAudienceKind.Workspace, 0).Items);
        Assert.Empty(feed.CatchUp(other, SurfaceAudienceKind.Principal, 0).Items);
        Assert.False(McpInoCommandHandler.TryGetPrompt(JsonSerializer.SerializeToElement(new { prompt = "x", workspaceId = "forged" }), out _));
    }

    [Fact]
    public async Task Mcp_effect_handler_rejects_cross_workspace_aggregate()
    {
        var context = new RuntimeRequestContext(new("tenant-a"), new("workspace-a"), new("user-a", PrincipalKind.User), "session", AuthAssurance.Password, "corr", null, new HashSet<string> { "brain.act" });
        var command = new CommandEnvelope("effect.execute", 2, "cmd-1", context,
            JsonSerializer.SerializeToElement(new { aggregateId = "v2:tenant-a:workspace-b:workflow:w1", effectId = "effect-1" }));
        var handler = new McpEffectCommandHandler(new NoopEffectPort());

        var result = await handler.ExecuteAsync(command);

        Assert.Equal(WorkflowState.Failed, result.State);
        Assert.Equal("effect-scope-invalid", result.SafeReason);
    }

    private sealed class NoopEffectPort : IEffectWorkerPort
    {
        public Task<EffectTransitionRecord> ExecuteAsync(string aggregateId, string effectId, string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
            => throw new Xunit.Sdk.XunitException("The port must not be called for a cross-workspace aggregate.");
    }

    [Fact]
    public void Grain_ids_are_canonical_and_scoped()
    {
        var a = GrainIds.Conversation(new("t1"), new("w1"), "c1");
        var b = GrainIds.Conversation(new("t1"), new("w2"), "c1");
        Assert.NotEqual(a, b);
        Assert.StartsWith(GrainIds.ScopePrefix(new("t1"), new("w1")), a, StringComparison.Ordinal);
        Assert.NotEqual(
            GrainIds.Aggregate(new("a:b"), new("c"), "same"),
            GrainIds.Aggregate(new("a"), new("b:c"), "same"));
    }

    [Fact]
    public void Isolation_gate_fails_closed()
    {
        var gate = new CapabilityIsolationGate();
        var context = new RuntimeRequestContext(new("t1"), new("w1"), new("u1", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        Assert.True(gate.IsAllowed(context, new("t1"), new("w1"), "brain.read"));
        Assert.False(gate.IsAllowed(context, new("t1"), new("w2"), "brain.read"));
        Assert.Throws<UnauthorizedAccessException>((Action)(() => gate.Demand(context, new("t1"), new("w1"), "brain.act")));
    }

    [Fact]
    public void Commit_seal_is_deterministic_and_secret_summary_redacts()
    {
        var payload = JsonDocument.Parse("{\"ok\":true}").RootElement.Clone();
        var events = new[] { new EventEnvelope("v2.test", 1, "e1", "c1", null, payload) };
        Assert.Equal(CommitSeal.Compute(events), CommitSeal.Compute(events));
        Assert.Equal("[REDACTED]", Redaction.SafeSummary("secret", Sensitivity.Secret));
    }

    [Fact]
    public void Approval_queues_apply_without_quiescent_approved_state()
    {
        var workflow = new Workflow();
        workflow.SubmitForApproval();
        workflow.Approve(new ApprovalRecord(new("operator", PrincipalKind.Operator), DateTimeOffset.UtcNow, "d1", null));
        Assert.Equal(WorkflowState.ApplyQueued, workflow.State);
        workflow.BeginApply();
        workflow.Succeed();
        Assert.Equal(WorkflowState.Succeeded, workflow.State);
        Assert.Equal(new[] { WorkflowState.AwaitingApproval, WorkflowState.Approved, WorkflowState.ApplyQueued, WorkflowState.Applying, WorkflowState.Succeeded }, workflow.Transitions.Select(x => x.To));
        Assert.Equal("operator", workflow.Approval!.Approver.Value);
    }

    [Fact]
    public void Workflow_reject_expire_cancel_and_approval_guards_are_fail_closed()
    {
        var notAwaiting = new Workflow();
        Assert.Throws<InvalidOperationException>(() => notAwaiting.Approve(new ApprovalRecord(new("operator", PrincipalKind.Operator), DateTimeOffset.UtcNow, "d", null)));

        var rejected = new Workflow();
        rejected.SubmitForApproval();
        rejected.Reject("policy denied");
        Assert.Equal(WorkflowState.Rejected, rejected.State);

        var expired = new Workflow();
        expired.SubmitForApproval();
        expired.Expire();
        Assert.Equal(WorkflowState.Expired, expired.State);

        var cancelled = new Workflow();
        cancelled.Cancel();
        Assert.Equal(WorkflowState.Cancelled, cancelled.State);
    }

    [Fact]
    public async Task Durable_workflow_approval_persists_authenticated_audit_and_apply_queue()
    {
        var store = new InMemoryAggregateStore();
        var aggregate = new WorkflowAggregate(store);
        var context = new RuntimeRequestContext(new("t"), new("w"), new("operator", PrincipalKind.Operator), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.approve" });
        await aggregate.SubmitForApprovalAsync("proposal-1", "submit-1", context);
        var approval = new ApprovalRecord(context.Principal, DateTimeOffset.UtcNow, "decision-1", "safe");
        var effect = new OutboxRecord("effect-1", "operation-1", 0, "fake", System.Text.Json.JsonDocument.Parse("{}").RootElement, DateTimeOffset.UtcNow.AddMinutes(5));
        var snapshot = await aggregate.ApproveAsync("proposal-1", "approve-1", context, approval, effect);
        Assert.Contains(snapshot.Commits.SelectMany(x => x.Events), x => x.Type == "v2.workflow.ApplyQueued");
        Assert.Contains(snapshot.Outbox, x => x.EffectId == "effect-1");
        var persisted = System.Text.Json.JsonSerializer.Deserialize<WorkflowPersistedState>(snapshot.State.GetRawText(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(WorkflowState.ApplyQueued, persisted.State);
        Assert.Equal("operator", persisted.Approval!.Approver.Value);
        Assert.Equal(new[] { WorkflowState.AwaitingApproval, WorkflowState.Approved, WorkflowState.ApplyQueued }, persisted.Transitions.Select(x => x.To));
        snapshot = await aggregate.AdvanceAsync("proposal-1", "apply-1", context, WorkflowState.Applying);
        snapshot = await aggregate.AdvanceAsync("proposal-1", "unknown-1", context, WorkflowState.OutcomeUnknown, "provider-timeout");
        snapshot = await aggregate.AdvanceAsync("proposal-1", "manual-1", context, WorkflowState.ManualIntervention, "operator-review");
        var finalState = System.Text.Json.JsonSerializer.Deserialize<WorkflowPersistedState>(snapshot.State.GetRawText(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(WorkflowState.ManualIntervention, finalState.State);
    }

    [Fact]
    public async Task Durable_workflow_survives_file_store_reopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "db-v2-workflow-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = new RuntimeRequestContext(new("t"), new("w"), new("operator", PrincipalKind.Operator), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.approve" });
            var first = new WorkflowAggregate(new FileAggregateStore(root));
            await first.SubmitForApprovalAsync("proposal", "submit", context);
            await first.ApproveAsync("proposal", "approve", context, new ApprovalRecord(context.Principal, DateTimeOffset.UtcNow, "decision", null), new OutboxRecord("effect", "operation", 0, "fake", System.Text.Json.JsonDocument.Parse("{}").RootElement, DateTimeOffset.UtcNow.AddMinutes(1)));
            var reopened = await new FileAggregateStore(root).ReadAsync("proposal");
            Assert.Equal(2, reopened.CommitSequence);
            Assert.Single(reopened.Outbox);
            Assert.Contains(reopened.Commits.SelectMany(x => x.Events), x => x.Type == "v2.workflow.ApplyQueued");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void schema_registry_is_stable_and_fail_closed()
    {
        var registry = new SchemaRegistry([new SchemaDescriptor("v2.workflow.ApplyQueued", 2, "Operational", true)]);
        Assert.True(registry.TryResolve("v2.workflow.ApplyQueued", 2, out _));
        Assert.Throws<InvalidOperationException>(() => registry.Require("v2.unknown", 1));
        Assert.Throws<InvalidOperationException>(() => registry.Register(new SchemaDescriptor("v2.workflow.ApplyQueued", 2, "Secret", false)));
    }

    [Fact]
    public async Task Projection_scans_registered_owners_and_checkpoints()
    {
        var source = new InMemoryCommitSource();
        source.RegisterOwner("owner-1");
        var payload = JsonDocument.Parse("{\"ok\":true}").RootElement.Clone();
        var evt = new EventEnvelope("v2.test", 1, "event-1", "corr-1", null, payload);
        source.Append("owner-1", new AggregateCommit(1, "commit-1", [evt], CommitSeal.Compute([evt]), DateTimeOffset.UtcNow));
        var sink = new InMemoryProjectionSink("timeline");
        var applied = await new ProjectionWorker(source, sink).RunFullCycleAsync(new DirectoryScanCursor(0, 0));
        Assert.Equal(1, applied);
        Assert.Single(sink.Applied);
        Assert.Empty(sink.Poison);
    }

    [Fact]
    public void Session_tokens_expire_and_revoke()
    {
        var service = new SessionTokenService(new byte[32]);
        var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        var token = service.Issue(context, TimeSpan.FromMinutes(1));
        Assert.True(service.TryValidate(token, out var restored));
        Assert.Equal("t", restored.TenantId.Value);
        service.Revoke("s");
        Assert.False(service.TryValidate(token, out _));
    }

    [Fact]
    public void Production_manifest_fails_closed_for_mutations_and_stdio()
    {
        var manifest = CapabilityManifests.For(RuntimeProfile.Production);
        Assert.False(manifest.HttpMcpMutations);
        Assert.False(manifest.TrustedStdioMcp);
        Assert.Contains("brain.admin", manifest.Disabled);
    }

    [Fact]
    public async Task Application_port_scopes_operations_and_idempotency_to_the_full_principal()
    {
        var service = new ApplicationService(capabilities:
        [
            new Capability("brain.read", 2, true, false),
            new Capability("brain.act", 2, true, true)
        ]);
        var grants = new HashSet<string> { "brain.read", "brain.act" };
        var context = new RuntimeRequestContext(new("tenant"), new("workspace-a"), new("user-a", PrincipalKind.User), "session", AuthAssurance.Password, "corr", "idem-1", grants);
        var payload = JsonDocument.Parse("{\"prompt\":\"hello\"}").RootElement.Clone();
        var first = await service.SubmitAsync(context, new CommandEnvelope("noop", 2, "cmd-1", context, payload));
        var second = await service.SubmitAsync(context, new CommandEnvelope("noop", 2, "cmd-2", context, payload));
        Assert.Equal(first.OperationId, second.OperationId);

        var otherPrincipal = context with { Principal = new PrincipalRef("user-b", PrincipalKind.User), SessionId = "session-b" };
        var otherKind = context with { Principal = new PrincipalRef("user-a", PrincipalKind.Service), SessionId = "session-service" };
        var otherPrincipalOperation = await service.SubmitAsync(otherPrincipal,
            new CommandEnvelope("noop", 2, "cmd-3", otherPrincipal, payload));
        var otherKindOperation = await service.SubmitAsync(otherKind,
            new CommandEnvelope("noop", 2, "cmd-4", otherKind, payload));

        Assert.NotEqual(first.OperationId, otherPrincipalOperation.OperationId);
        Assert.NotEqual(first.OperationId, otherKindOperation.OperationId);
        Assert.Null(await service.GetOperationAsync(otherPrincipal, first.OperationId));
        Assert.Null(await service.GetOperationAsync(otherKind, first.OperationId));
        Assert.Equal(otherPrincipalOperation.OperationId,
            Assert.Single((await service.GetOperationsAsync(otherPrincipal, null, 10)).Items).OperationId);
        Assert.Equal(otherKindOperation.OperationId,
            Assert.Single((await service.GetOperationsAsync(otherKind, null, 10)).Items).OperationId);
    }

    [Fact]
    public async Task Application_port_replays_only_the_same_type_version_and_canonical_input()
    {
        var service = new ApplicationService();
        var context = new RuntimeRequestContext(new("tenant"), new("workspace"), new("user", PrincipalKind.User), "session",
            AuthAssurance.Password, "corr", "idem", new HashSet<string> { "brain.act", "brain.read" });
        var firstPayload = JsonDocument.Parse("{\"prompt\":\"hello\",\"options\":{\"b\":2,\"a\":1}}").RootElement.Clone();
        var reorderedPayload = JsonDocument.Parse("{\"options\":{\"a\":1,\"b\":2},\"prompt\":\"hello\"}").RootElement.Clone();
        var first = await service.SubmitAsync(context, new CommandEnvelope("ino.interact", 2, "cmd-1", context, firstPayload));
        var replay = await service.SubmitAsync(context, new CommandEnvelope("ino.interact", 2, "cmd-2", context, reorderedPayload));

        Assert.Equal(first.OperationId, replay.OperationId);
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => service.SubmitAsync(context,
            new CommandEnvelope("ino.interact", 2, "cmd-3", context,
                JsonDocument.Parse("{\"prompt\":\"changed\",\"options\":{\"a\":1,\"b\":2}}").RootElement.Clone())));
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => service.SubmitAsync(context,
            new CommandEnvelope("ino.other", 2, "cmd-4", context, reorderedPayload)));
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => service.SubmitAsync(context,
            new CommandEnvelope("ino.interact", 3, "cmd-5", context, reorderedPayload)));

        var forgedContext = context with { Principal = new PrincipalRef("other", PrincipalKind.User) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SubmitAsync(context,
            new CommandEnvelope("ino.interact", 2, "cmd-6", forgedContext, reorderedPayload)));
    }

    [Fact]
    public async Task Application_port_rejects_tampered_cursor()
    {
        var service = new ApplicationService();
        var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetCapabilitiesAsync(context, "tampered", 10));
    }

    [Fact]
    public async Task Aggregate_store_commits_contiguously_and_deduplicates_inbox()
    {
        var store = new InMemoryAggregateStore();
        var payload = JsonDocument.Parse("{\"value\":1}").RootElement.Clone();
        var evt = new EventEnvelope("v2.state.changed", 1, "event-1", "corr", null, payload);
        var request = new V2CommitRequest("command-1", 0, payload, [evt], [new OutboxRecord("effect-1", "operation-1", 0, "fake", payload, DateTimeOffset.UtcNow.AddMinutes(1))], DateTimeOffset.UtcNow);
        var first = await store.CommitAsync("aggregate-1", request);
        var duplicate = await store.CommitAsync("aggregate-1", request);
        Assert.True(first.Accepted);
        Assert.True(duplicate.Duplicate);
        Assert.Equal(first.Commit.CommitId, duplicate.Commit.CommitId);
        Assert.Single(duplicate.Snapshot.Outbox);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitAsync("aggregate-1", request with { CommandId = "command-2" }));
    }

    [Fact]
    public async Task Effect_transition_history_is_append_only_and_idempotent()
    {
        var store = new InMemoryAggregateStore();
        var transition = new EffectTransitionRecord("effect", "transition-1", "Applying", "safe", DateTimeOffset.UtcNow);
        await store.AppendEffectTransitionAsync("aggregate", transition);
        await store.AppendEffectTransitionAsync("aggregate", transition);
        Assert.Single((await store.ReadAsync("aggregate")).EffectTransitions);
    }

    [Fact]
    public async Task File_store_reopens_without_touching_non_v2_namespace()
    {
        var root = Path.Combine(Path.GetTempPath(), "db-v2-" + Guid.NewGuid().ToString("N"));
        try
        {
            var payload = JsonDocument.Parse("{\"v\":1}").RootElement.Clone();
            var evt = new EventEnvelope("v2.changed", 1, "e", "c", null, payload);
            var store = new FileAggregateStore(root);
            await store.CommitAsync("a", new V2CommitRequest("cmd", 0, payload, [evt], [], DateTimeOffset.UtcNow));
            var reopened = new FileAggregateStore(root);
            var snapshot = await reopened.ReadAsync("a");
            Assert.Equal(1, snapshot.CommitSequence);
            Assert.True(Directory.Exists(Path.Combine(root, "v2-aggregates")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Effect_coordinator_marks_unknown_without_retrying()
    {
        var store = new InMemoryAggregateStore();
        var payload = JsonDocument.Parse("{\"x\":1}").RootElement.Clone();
        var evt = new EventEnvelope("v2.effect", 1, "e", "c", null, payload);
        await store.CommitAsync("aggregate", new V2CommitRequest("cmd", 0, payload, [evt], [new OutboxRecord("effect", "op", 0, "fake", payload, DateTimeOffset.UtcNow.AddMinutes(5))], DateTimeOffset.UtcNow));
        var handler = new FakeEffectHandler(EffectDisposition.OutcomeUnknown);
        var coordinator = new EffectCoordinator(store, [handler]);
        var result = await coordinator.ExecuteOnceAsync("aggregate", "effect", "worker", TimeSpan.FromMinutes(1));
        Assert.Equal("OutcomeUnknown", result.State);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task File_projection_sink_reopens_checkpoint_and_quarantine()
    {
        var root = Path.Combine(Path.GetTempPath(), "projection-v2-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sink = new FileProjectionSink(root, "timeline");
            var checkpoint = new ProjectionCheckpoint("timeline", "owner", 4, 2);
            await sink.SaveCheckpointAsync(checkpoint);
            await sink.QuarantineAsync(new PoisonRecord("timeline", "owner", 5, "checksum", DateTimeOffset.UtcNow));
            var reopened = new FileProjectionSink(root, "timeline");
            Assert.Equal(4, (await reopened.ReadCheckpointAsync("owner"))!.CommitSequence);
            Assert.True(Directory.GetFiles(Path.Combine(root, "v2-projections", "timeline", "quarantine")).Length == 1);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Projection_query_port_is_scoped_and_opaque_cursor_paginated()
    {
        var store = new InMemoryProjectionQueryStore();
        var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        store.Add(new TimelineEntry("1", new("t"), new("w"), DateTimeOffset.UtcNow, "Event", "safe", "c"));
        store.Add(new TimelineEntry("2", new("other"), new("w"), DateTimeOffset.UtcNow, "Event", "hidden", "c"));
        var page = await store.TimelineAsync(context, null, 10);
        Assert.Single(page.Items);
        Assert.DoesNotContain("other", System.Text.Json.JsonSerializer.Serialize(page.Items));
    }

    private sealed class FakeEffectHandler(EffectDisposition disposition) : IEffectHandler
    {
        public string EffectType => "fake";
        public int Calls { get; private set; }
        public Task<EffectExecutionResult> ExecuteAsync(OutboxRecord intent, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new EffectExecutionResult(disposition, "safe-result"));
        }
    }

    [Fact]
    public void Orleans_v2_aggregate_contract_has_stable_aliases()
    {
        var alias = typeof(IAggregateGrain).GetCustomAttributes(typeof(AliasAttribute), false).Cast<AliasAttribute>().Single();
        Assert.Equal("digitalbrain.v2.aggregate-grain", alias.Alias);
        var workerAlias = typeof(IEffectWorkerGrain).GetCustomAttributes(typeof(AliasAttribute), false).Cast<AliasAttribute>().Single();
        Assert.Equal("digitalbrain.v2.effect-worker-grain", workerAlias.Alias);
        var conversationModelAlias = typeof(IConversationModelGrain)
            .GetCustomAttributes(typeof(AliasAttribute), false).Cast<AliasAttribute>().Single();
        Assert.Equal("digitalbrain.v2.conversation-model-grain", conversationModelAlias.Alias);
    }

    [Fact]
    public async Task Scoped_conversation_owner_rejects_context_escape_and_keeps_tool_contract_structured()
    {
        var context = new RuntimeRequestContext(new("tenant"), new("workspace"), new("user", PrincipalKind.User), "session", AuthAssurance.Password, "corr", null, new HashSet<string> { "brain.read" });
        var owner = new ConversationOwner(new FakeContextAssembler(), new FakePlanner(), new FakeModelRouter(), new FakeToolCatalog(), new FakeComposer());
        var result = await owner.ExecuteAsync(new ConversationRequest(context, "conversation", "hello"));
        Assert.Equal(":Success", result);
    }

    [Fact]
    public void Session_refresh_is_one_use_and_revoke_invalidates_access_session()
    {
        var tokens = new SessionTokenService(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var manager = new SessionManager(tokens, TimeSpan.FromHours(1));
        var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "session", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        var pair = manager.Create(context, TimeSpan.FromMinutes(5));
        Assert.True(manager.TryRefresh(pair.RefreshToken, TimeSpan.FromMinutes(5), out var rotated));
        Assert.NotEqual(pair.RefreshToken, rotated.RefreshToken);
        Assert.False(manager.TryRefresh(pair.RefreshToken, TimeSpan.FromMinutes(5), out _));
        Assert.True(manager.Revoke(rotated.RefreshToken));
        Assert.False(tokens.TryValidate(rotated.AccessToken, out _));
    }

    [Fact]
    public void File_session_rotation_and_logout_failures_do_not_consume_unpersisted_state()
    {
        var fail = false;
        var tokens = new SessionTokenService(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var journalKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var path = Path.Combine(Path.GetTempPath(), "v2-session-seam-" + Guid.NewGuid().ToString("N"), "sessions.jsonl");
        var manager = new FileSessionManager(
            new AuthenticatedJournalFaultInjection(BeforePhysicalAppend: () =>
            {
                if (fail) throw new IOException("injected session journal failure");
            }),
            tokens,
            path,
            journalIntegrityKey: journalKey);
        var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "session",
            AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        var createFails = true;
        var createManager = new FileSessionManager(
            new AuthenticatedJournalFaultInjection(BeforePhysicalAppend: () =>
            {
                if (createFails) throw new IOException("injected create journal failure");
            }),
            tokens,
            path + ".create",
            journalIntegrityKey: journalKey);
        Assert.Throws<IOException>(() => createManager.Create(context with { SessionId = "create-session" },
            TimeSpan.FromMinutes(5), SessionAudiences.Ui));
        createFails = false;
        _ = createManager.Create(context with { SessionId = "create-session" }, TimeSpan.FromMinutes(5), SessionAudiences.Ui);
        var pair = manager.Create(context, TimeSpan.FromMinutes(5), SessionAudiences.Ui);

        fail = true;
        Assert.Throws<IOException>(() => manager.TryRefresh(pair.RefreshToken, TimeSpan.FromMinutes(5), SessionAudiences.Ui, out _));
        fail = false;
        Assert.True(manager.TryRefresh(pair.RefreshToken, TimeSpan.FromMinutes(5), SessionAudiences.Ui, out var rotated));
        fail = true;
        Assert.Throws<IOException>(() => manager.Revoke(rotated.RefreshToken, SessionAudiences.Ui));
        Assert.True(tokens.TryValidate(rotated.AccessToken, SessionAudiences.Ui, out _));
        fail = false;
        Assert.True(manager.Revoke(rotated.RefreshToken, SessionAudiences.Ui));
        Assert.False(tokens.TryValidate(rotated.AccessToken, SessionAudiences.Ui, out _));
    }

    [Fact]
    public void File_session_journal_fails_closed_on_a_malformed_interior_rotation_record()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-session-malformed-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "sessions.jsonl");
            var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var manager = new FileSessionManager(new SessionTokenService(key), path, journalIntegrityKey: key);
            var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "session",
                AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
            var pair = manager.Create(context, TimeSpan.FromMinutes(5), SessionAudiences.Ui);
            Assert.True(manager.TryRefresh(pair.RefreshToken, TimeSpan.FromMinutes(5), SessionAudiences.Ui, out _));
            var lines = File.ReadAllLines(path).ToList();
            lines.Insert(1, "{private-refresh-token-marker");
            File.WriteAllLines(path, lines);

            Assert.Throws<InvalidDataException>(() => new FileSessionManager(new SessionTokenService(key), path, journalIntegrityKey: key));
            var quarantine = File.ReadAllText(path + ".quarantine");
            Assert.DoesNotContain("private-refresh-token-marker", quarantine, StringComparison.Ordinal);
            Assert.Contains("sha256", quarantine, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void File_session_journal_rejects_valid_json_with_an_incomplete_context()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-session-incomplete-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "sessions.jsonl");
            var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var manager = new FileSessionManager(new SessionTokenService(key), path, journalIntegrityKey: key);
            var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "session",
                AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
            manager.Create(context, TimeSpan.FromMinutes(5), SessionAudiences.Ui);
            var envelope = JsonNode.Parse(File.ReadAllText(path).Trim())!.AsObject();
            envelope["payload"]!["Entry"]!["Context"] = null;
            File.WriteAllText(path, envelope.ToJsonString());

            Assert.Throws<InvalidDataException>(() => new FileSessionManager(new SessionTokenService(key), path, journalIntegrityKey: key));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Private_feed_is_workspace_scoped_and_action_binding_is_single_use()
    {
        var feed = new PrivateFeedStore();
        var first = new RuntimeRequestContext(new("t"), new("w"), new("u1", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        var other = first with { WorkspaceId = new WorkspaceId("other"), Principal = new PrincipalRef("u2", PrincipalKind.User) };
        feed.Append(first, "surface", 1, "hash", System.Text.Json.JsonDocument.Parse("{\"safe\":true}").RootElement);
        feed.Append(first, "surface", 2, "hash2", System.Text.Json.JsonDocument.Parse("{\"safe\":true}").RootElement);
        Assert.Equal(2, feed.CatchUp(first, null).Items.Count);
        Assert.Empty(feed.CatchUp(other, null).Items);
        feed.RetainFrom(first, 2);
        Assert.True(feed.CatchUp(first, 0).ResetRequired);
        var token = "issued";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
        var actions = new ActionExecutor();
        actions.Register(new ActionBinding("b", "template", 1, "schema", 1, DateTimeOffset.UtcNow.AddMinutes(1), hash));
        var submission = actions.Use(first, "b", token, System.Text.Json.JsonDocument.Parse("{}").RootElement);
        Assert.True(actions.TryGetUse(submission.IdempotencyKey, out _));
        Assert.Throws<InvalidOperationException>(() => actions.Use(first, "b", token, System.Text.Json.JsonDocument.Parse("{}").RootElement));
    }

    [Fact]
    public void Model_router_enforces_privacy_residency_capabilities_and_bounded_fallback()
    {
        var models = new[]
        {
            new ModelDescriptor("cloud", true, true, true, false, false, "private", "eu", 0.01m, TimeSpan.FromMilliseconds(100)),
            new ModelDescriptor("local", true, true, true, true, true, "private", "eu", 0.02m, TimeSpan.FromMilliseconds(50)),
            new ModelDescriptor("unsafe", true, true, true, true, true, "public", "us", 0.001m, TimeSpan.FromMilliseconds(1))
        };
        var router = new ModelRouter(models);
        var selection = router.Select(new ModelPolicy("private", "eu", 0.03m, TimeSpan.FromSeconds(1), 512, true, true, true, true));
        Assert.Equal("local", selection.Key);
        Assert.Throws<InvalidOperationException>(() => router.Select(new ModelPolicy("private", "eu", 0.005m, TimeSpan.FromSeconds(1), 512, false, false, false, false)));
    }

    [Fact]
    public async Task Telemetry_keeps_metric_labels_low_cardinality_and_accounts_drops()
    {
        var telemetry = new TelemetryBuffer(1);
        await telemetry.EmitAsync(new MetricPoint("v2.outbox.age", 1, new Dictionary<string, string> { ["tenant"] = "secret-tenant", ["outcome"] = "success" }));
        await telemetry.EmitAsync(new MetricPoint("v2.outbox.age", 2, new Dictionary<string, string> { ["workspace"] = "secret-workspace", ["status"] = "retry" }));
        Assert.Equal(1, telemetry.Dropped);
        var point = Assert.Single(telemetry.Metrics);
        Assert.DoesNotContain("tenant", point.Labels.Keys);
        Assert.Equal("success", point.Labels["outcome"]);
        await telemetry.EmitTraceAsync(new TraceContext("trace", "span", new("t"), new("w"), "command", "operation"), "effect", "safe detail");
        Assert.Single(telemetry.Traces);
        await telemetry.EmitTraceAsync(new TraceContext("trace-2", "span-2", new("t"), new("w")), "overflow", "discarded");
        Assert.Equal(2, telemetry.Dropped);
        Assert.Single(telemetry.Traces);
    }

    [Fact]
    public void Deployment_preview_is_non_mutating_and_blocks_required_topology_drift()
    {
        var desired = new TopologySnapshot(new[] { new TopologyResource("kernel", "container-app", true, "Test", "sha256:good") }, "Test");
        var actual = new TopologySnapshot(new[] { new TopologyResource("kernel", "container-app", true, "Test", "sha256:old") }, "Test");
        var preview = DeploymentPreviewer.Preview(desired, actual);
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Drift, x => x.Resource == "kernel" && x.Blocking);
        Assert.Equal("sha256:old", actual.Resources[0].ImageDigest);
    }

    [Fact]
    public void Operator_bootstrap_is_single_use_and_admin_policy_is_fail_closed()
    {
        var signing = new SessionTokenService(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var manager = new SessionManager(signing);
        var bootstrap = new OperatorBootstrap(manager, new AdminPolicy(true, true, new HashSet<string> { "brain.admin" }));
        bootstrap.ConfigureBootstrapSecret("one-use-secret");
        Assert.True(bootstrap.TryConsume("one-use-secret", new("t"), new("w"), "operator", out var result));
        Assert.Equal(PrincipalKind.Operator, result!.Context.Principal.Kind);
        var signed = signing.Issue(result.Context, TimeSpan.FromMinutes(5));
        Assert.True(signing.TryValidate(signed, out var restored));
        Assert.Equal(PrincipalKind.Operator, restored.Principal.Kind);
        Assert.Equal(AuthAssurance.OperatorBootstrap, restored.Assurance);
        Assert.False(bootstrap.TryConsume("one-use-secret", new("t"), new("w"), "operator", out _));
        var disabled = new OperatorBootstrap(manager, new AdminPolicy(false, true, new HashSet<string> { "brain.admin" }));
        disabled.ConfigureBootstrapSecret("other");
        Assert.False(disabled.TryConsume("other", new("t"), new("w"), "operator", out _));
    }

    [Fact]
    public void Mcp_guard_enforces_audience_origin_body_rate_and_concurrency()
    {
        var guard = new McpRequestGuard(new McpTransportPolicy("aud", new HashSet<string> { "https://client" }, 10, 1, 1));
        Assert.False(guard.TryBegin("p", "https://other", "aud", 1, out _));
        Assert.False(guard.TryBegin("p", "https://client", "wrong", 1, out _));
        Assert.False(guard.TryBegin("p", "https://client", "aud", 11, out _));
        Assert.True(guard.TryBegin("p", "https://client", "aud", 1, out var lease));
        Assert.False(guard.TryBegin("p", "https://client", "aud", 1, out _));
        lease!.Dispose();
        Assert.False(guard.TryBegin("p", "https://client", "aud", 1, out _));
    }

    [Fact]
    public void Grpc_v2_auth_requires_audience_and_signed_session_metadata()
    {
        var tokens = new SessionTokenService(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        var token = tokens.Issue(context, TimeSpan.FromMinutes(5), "gateway");
        var metadata = new Dictionary<string, string> { ["x-v2-audience"] = "gateway", ["x-v2-session"] = token };
        Assert.True(GrpcAuthentication.TryAuthenticate(metadata, tokens, "gateway", out var authenticated));
        Assert.Equal(context.TenantId, authenticated.TenantId);
        metadata["x-v2-audience"] = "wrong";
        Assert.False(GrpcAuthentication.TryAuthenticate(metadata, tokens, "gateway", out _));
    }

    [Fact]
    public void File_feed_reopens_with_v2_items_after_restart()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-feed-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
            var first = new FilePrivateFeedStore(root);
            first.Append(context, "surface", 1, "hash", System.Text.Json.JsonDocument.Parse("{}").RootElement);
            var reopened = new FilePrivateFeedStore(root);
            Assert.Single(reopened.CatchUp(context, null).Items);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task operations_reopen_with_principal_ownership_and_exact_replay_receipt()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-ops-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", "idem", new HashSet<string> { "brain.act", "brain.read" });
            var journalKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var service = new ApplicationService(storagePath: Path.Combine(root, "operations.jsonl"), journalIntegrityKey: journalKey);
            var command = new CommandEnvelope("test", 2, "cmd", context, System.Text.Json.JsonDocument.Parse("{}").RootElement);
            var operation = await service.SubmitAsync(context, command);
            var persisted = await File.ReadAllTextAsync(Path.Combine(root, "operations.jsonl"));
            Assert.Contains("\"Command\"", persisted, StringComparison.Ordinal);
            Assert.Contains("\"Type\":\"test\"", persisted, StringComparison.Ordinal);
            var reopened = new ApplicationService(storagePath: Path.Combine(root, "operations.jsonl"), journalIntegrityKey: journalKey);
            Assert.NotNull(await reopened.GetOperationAsync(context, operation.OperationId));
            var other = context with { WorkspaceId = new WorkspaceId("other") };
            Assert.Null(await reopened.GetOperationAsync(other, operation.OperationId));
            var otherPrincipal = context with { Principal = new PrincipalRef("other", PrincipalKind.User) };
            Assert.Null(await reopened.GetOperationAsync(otherPrincipal, operation.OperationId));
            var replay = await reopened.SubmitAsync(context, command with { CommandId = "cmd-after-restart" });
            Assert.Equal(operation.OperationId, replay.OperationId);
            await Assert.ThrowsAsync<IdempotencyConflictException>(() => reopened.SubmitAsync(context,
                command with { CommandId = "changed-after-restart", Payload = JsonSerializer.SerializeToElement(new { changed = true }) }));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task operation_submission_is_concurrent_idempotent_and_not_visible_before_durable_append()
    {
        var fail = true;
        var writes = 0;
        var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s",
            AuthAssurance.Password, "c", "same-idempotency", new HashSet<string> { "brain.act", "brain.read" });
        var command = new CommandEnvelope("test", 2, "command", context, JsonSerializer.SerializeToElement(new { }));
        var journalKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var path = Path.Combine(Path.GetTempPath(), "runtime-operation-seam-" + Guid.NewGuid().ToString("N"), "operations.jsonl");
        var service = new ApplicationService(
            new AuthenticatedJournalFaultInjection(BeforePhysicalAppend: () =>
            {
                if (fail) throw new IOException("injected operation journal failure");
                Interlocked.Increment(ref writes);
            }),
            storagePath: path,
            journalIntegrityKey: journalKey);

        await Assert.ThrowsAsync<IOException>(() => service.SubmitAsync(context, command));
        Assert.Empty(service.GetPendingOperationIds());
        fail = false;
        var submissions = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => service.SubmitAsync(context, command))).ToArray());
        Assert.Single(submissions.Select(static operation => operation.OperationId).Distinct(StringComparer.Ordinal));
        Assert.Single(service.GetPendingOperationIds());
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task operation_idempotency_scope_is_unambiguous_for_delimiter_bearing_identifiers()
    {
        var service = new ApplicationService();
        var first = new RuntimeRequestContext(new("a:b"), new("c"), new("same", PrincipalKind.User), "s1",
            AuthAssurance.Password, "c1", "idem", new HashSet<string> { "brain.act", "brain.read" });
        var second = new RuntimeRequestContext(new("a"), new("b:c"), new("same", PrincipalKind.User), "s2",
            AuthAssurance.Password, "c2", "idem", new HashSet<string> { "brain.act", "brain.read" });
        var firstOperation = await service.SubmitAsync(first,
            new CommandEnvelope("test", 2, "command", first, JsonSerializer.SerializeToElement(new { })));
        var secondOperation = await service.SubmitAsync(second,
            new CommandEnvelope("test", 2, "command", second, JsonSerializer.SerializeToElement(new { })));

        Assert.NotEqual(firstOperation.OperationId, secondOperation.OperationId);
        Assert.NotNull(await service.GetOperationAsync(first, firstOperation.OperationId));
        Assert.Null(await service.GetOperationAsync(first, secondOperation.OperationId));
        Assert.NotEqual(RequestScope.Id(first), RequestScope.Id(second));
    }

    [Fact]
    public void operations_fail_closed_on_a_torn_journal_record()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-torn-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "operations.jsonl");
            File.WriteAllText(path, "{not-json}\n");
            var journalKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            Assert.Throws<InvalidDataException>(() => new ApplicationService(storagePath: path, journalIntegrityKey: journalKey));
            var quarantine = File.ReadAllText(path + ".quarantine");
            Assert.Contains("invalid-json", quarantine);
            Assert.DoesNotContain("not-json", quarantine);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task File_projection_store_recovers_and_keeps_workspaces_isolated()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-proj-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "projections.jsonl");
            var store = new FileProjectionQueryStore(path);
            var first = new RuntimeRequestContext(new("t"), new("w1"), new("u1", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
            var other = first with { WorkspaceId = new WorkspaceId("w2"), Principal = new PrincipalRef("u2", PrincipalKind.User) };
            store.Add(new TimelineEntry("a", first.TenantId, first.WorkspaceId, DateTimeOffset.UtcNow, "test", "safe", "c"));
            store.Add(new TimelineEntry("b", other.TenantId, other.WorkspaceId, DateTimeOffset.UtcNow, "test", "safe", "c"));
            var reopened = new FileProjectionQueryStore(path);
            Assert.Single((await reopened.TimelineAsync(first, null, 10)).Items);
            Assert.Single((await reopened.TimelineAsync(other, null, 10)).Items);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Production_capability_manifest_does_not_advertise_mutation_capabilities()
    {
        Assert.Contains("brain.admin", CapabilityManifests.For(RuntimeProfile.Development).Enabled);
        var manifest = CapabilityManifests.For(RuntimeProfile.Production);
        var service = new ApplicationService(capabilities: manifest.Enabled.Where(x => x.StartsWith("brain.", StringComparison.Ordinal)).Select(x => new Capability(x, 2, true, x != "brain.read")));
        var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        var page = await service.GetCapabilitiesAsync(context, null, 20);
        Assert.DoesNotContain(page.Items, x => x.Id is "brain.act" or "brain.approve" or "brain.admin");
    }

    [Fact]
    public void Test_profile_disables_trusted_stdio_and_http_mutations()
    {
        var manifest = CapabilityManifests.For(RuntimeProfile.Test);
        Assert.False(manifest.TrustedStdioMcp);
        Assert.False(manifest.HttpMcpMutations);
        Assert.DoesNotContain("brain.admin", manifest.Enabled);
    }

    [Fact]
    public async Task Command_port_requires_capability_specific_to_command_kind()
    {
        var service = new ApplicationService();
        var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.act" });
        var approval = new CommandEnvelope("approve.proposal", 2, "cmd-approval", context, System.Text.Json.JsonDocument.Parse("{}").RootElement);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SubmitAsync(context, approval));
        var action = new CommandEnvelope("connector.send", 2, "cmd-action", context, System.Text.Json.JsonDocument.Parse("{}").RootElement);
        Assert.NotNull(await service.SubmitAsync(context, action));
    }

    [Fact]
    public async Task Durable_command_can_be_claimed_once_and_outcome_replayed()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-commands-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.act", "brain.read" });
            var journalKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var service = new ApplicationService(storagePath: Path.Combine(root, "operations.jsonl"), journalIntegrityKey: journalKey);
            var submitted = await service.SubmitAsync(context, new CommandEnvelope("connector.send", 2, "cmd-durable", context, System.Text.Json.JsonDocument.Parse("{}").RootElement));
            Assert.True(service.TryClaimPending(submitted.OperationId, out var command));
            Assert.Equal("cmd-durable", command!.CommandId);
            Assert.False(service.TryClaimPending(submitted.OperationId, out _));
            Assert.True(service.RecordOutcome(submitted.OperationId, WorkflowState.OutcomeUnknown, "provider outcome unavailable"));
            var reopened = new ApplicationService(storagePath: Path.Combine(root, "operations.jsonl"), journalIntegrityKey: journalKey);
            var status = await reopened.GetOperationAsync(context, submitted.OperationId);
            Assert.Equal(WorkflowState.OutcomeUnknown, status!.State);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Applying_command_reopens_as_outcome_unknown_without_being_requeued()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-applying-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "operations.jsonl");
            var journalKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s",
                AuthAssurance.Password, "c", "idem", new HashSet<string> { "brain.act", "brain.read" });
            var service = new ApplicationService(storagePath: path, journalIntegrityKey: journalKey);
            var submitted = await service.SubmitAsync(context,
                new CommandEnvelope("connector.send", 2, "cmd", context, JsonSerializer.SerializeToElement(new { })));
            Assert.True(service.TryClaimPending(submitted.OperationId, out _));

            var reopened = new ApplicationService(storagePath: path, journalIntegrityKey: journalKey);
            var recovered = await reopened.GetOperationAsync(context, submitted.OperationId);
            Assert.Equal(WorkflowState.OutcomeUnknown, recovered!.State);
            Assert.Equal("The previous attempt ended before its outcome was confirmed.", recovered.SafeReason);
            Assert.DoesNotContain(submitted.OperationId, recovered.SafeReason, StringComparison.Ordinal);
            Assert.Empty(reopened.GetPendingOperationIds());
            var lineCount = File.ReadLines(path).Count();

            var reopenedAgain = new ApplicationService(storagePath: path, journalIntegrityKey: journalKey);
            Assert.Equal(WorkflowState.OutcomeUnknown,
                (await reopenedAgain.GetOperationAsync(context, submitted.OperationId))!.State);
            Assert.Equal(lineCount, File.ReadLines(path).Count());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Claimed_ino_command_reopens_queued_and_completes_exactly_one_conversation()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-ino-claimed-" + Guid.NewGuid().ToString("N"));
        try
        {
            var operationsPath = Path.Combine(root, "operations.jsonl");
            var conversationPath = Path.Combine(root, "conversation.jsonl");
            var journalKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s",
                AuthAssurance.Password, "c", "ino-idem", new HashSet<string> { "brain.act", "brain.read" });
            var command = new CommandEnvelope(McpInoCommandHandler.CommandType, 2, "ino-command", context,
                JsonSerializer.SerializeToElement(new { prompt = "What can you help me with in this workspace?" }));
            var beforeCrash = new ApplicationService(storagePath: operationsPath, journalIntegrityKey: journalKey);
            var submitted = await beforeCrash.SubmitAsync(context, command);

            Assert.True(beforeCrash.TryClaimPending(submitted.OperationId, out _));

            var firstRecovery = new ApplicationService(storagePath: operationsPath, journalIntegrityKey: journalKey);
            var recoveredStatus = await firstRecovery.GetOperationAsync(context, submitted.OperationId);
            Assert.Equal(WorkflowState.ApplyQueued, recoveredStatus!.State);
            Assert.Null(recoveredStatus.SafeReason);
            Assert.Equal(new[] { submitted.OperationId }, firstRecovery.GetPendingOperationIds());

            var recovered = new ApplicationService(storagePath: operationsPath, journalIntegrityKey: journalKey);
            Assert.Equal(new[] { submitted.OperationId }, recovered.GetPendingOperationIds());
            Assert.Equal(submitted.OperationId, (await recovered.SubmitAsync(context, command)).OperationId);

            var effects = new InoEffectStore(conversationPath);
            var feed = new PrivateFeedStore(Path.Combine(root, "feed.jsonl"));
            var surfaces = new WorkspaceSurfaceProducer(feed, new ActionExecutor(feed), effects);
            var owner = new ConversationOwner(
                new McpConversationContextAssembler(effects),
                new McpNoToolPlanner(),
                new FakeModelRouter(),
                new McpNoToolCatalog(),
                new McpResponseComposer());
            var handler = new McpInoCommandHandler(effects, surfaces, owner);
            var dispatcher = new CommandDispatcher(recovered, [handler]);

            Assert.True(await dispatcher.DispatchAsync(submitted.OperationId));
            Assert.False(await dispatcher.DispatchAsync(submitted.OperationId));
            Assert.Equal(WorkflowState.Succeeded,
                (await recovered.GetOperationAsync(context, submitted.OperationId))!.State);
            Assert.Collection(effects.Read(context).Turns,
                user => Assert.Equal("user", user.Role),
                assistant => Assert.Equal("assistant", assistant.Role));
            Assert.Equal(2, new InoEffectStore(conversationPath).Read(context).Turns.Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Command_dispatcher_routes_once_and_marks_unknown_without_retry()
    {
        var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.act", "brain.read" });
        var service = new ApplicationService();
        var submitted = await service.SubmitAsync(context, new CommandEnvelope("test.command", 2, "cmd-dispatch", context, System.Text.Json.JsonDocument.Parse("{}").RootElement));
        var dispatcher = new CommandDispatcher(service, Array.Empty<ICommandHandler>());
        Assert.True(await dispatcher.DispatchAsync(submitted.OperationId));
        Assert.False(await dispatcher.DispatchAsync(submitted.OperationId));
        Assert.Equal(WorkflowState.ManualIntervention, (await service.GetOperationAsync(context, submitted.OperationId))!.State);
    }

    [Fact]
    public void File_session_manager_replays_rotation_and_revocation_without_plaintext_tokens()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-sessions-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "sessions.jsonl");
            var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var journalKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var tokens = new SessionTokenService(key);
            var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
            var first = new FileSessionManager(tokens, path, TimeSpan.FromHours(1), journalIntegrityKey: journalKey);
            var pair = first.Create(context, TimeSpan.FromMinutes(5));
            var reopened = new FileSessionManager(tokens, path, TimeSpan.FromHours(1), journalIntegrityKey: journalKey);
            Assert.True(reopened.TryRefresh(pair.RefreshToken, TimeSpan.FromMinutes(5), out var rotated));
            var third = new FileSessionManager(tokens, path, TimeSpan.FromHours(1), journalIntegrityKey: journalKey);
            Assert.False(third.TryRefresh(pair.RefreshToken, TimeSpan.FromMinutes(5), out _));
            Assert.True(third.Revoke(rotated.RefreshToken));
            var restartedTokens = new SessionTokenService(key);
            var final = new FileSessionManager(restartedTokens, path, TimeSpan.FromHours(1), journalIntegrityKey: journalKey);
            Assert.False(final.Revoke(rotated.RefreshToken));
            Assert.False(restartedTokens.TryValidate(rotated.AccessToken, SessionAudiences.Mcp, out _));
            Assert.DoesNotContain(pair.RefreshToken, File.ReadAllText(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("create")]
    [InlineData("rotate")]
    [InlineData("logout")]
    public void File_session_journal_rejects_forged_authority_records(string recordKind)
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-session-forge-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "sessions.jsonl");
            var signingKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var journalKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var tokens = new SessionTokenService(signingKey);
            var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "session",
                AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
            var manager = new FileSessionManager(tokens, path, journalIntegrityKey: journalKey);
            var created = manager.Create(context, TimeSpan.FromMinutes(5), SessionAudiences.Ui);
            if (recordKind == "rotate")
                Assert.True(manager.TryRefresh(created.RefreshToken, TimeSpan.FromMinutes(5), SessionAudiences.Ui, out _));
            else if (recordKind == "logout")
                Assert.True(manager.Revoke(created.RefreshToken, SessionAudiences.Ui));

            var lines = File.ReadAllLines(path);
            var index = Array.FindIndex(lines, line =>
                string.Equals(AuthenticatedPayload(line)["Kind"]!.GetValue<string>(), recordKind, StringComparison.Ordinal));
            Assert.True(index >= 0);
            var forged = JsonNode.Parse(lines[index])!.AsObject();
            forged["payload"]!["Hash"] = new string('A', 64);
            lines[index] = forged.ToJsonString();
            File.WriteAllLines(path, lines);

            Assert.Throws<InvalidDataException>(() =>
                new FileSessionManager(new SessionTokenService(signingKey), path, journalIntegrityKey: journalKey));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Operation_journal_rejects_an_unsigned_forged_queued_operation()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-operation-forge-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "operations.jsonl");
            var journalKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var context = DurableOperationContext();
            var service = new ApplicationService(storagePath: path, journalIntegrityKey: journalKey);
            await service.SubmitAsync(context, DurableOperationCommand(context));
            var payload = AuthenticatedPayload(File.ReadAllLines(path)[0]);
            payload["Operation"]!["OperationId"] = "v2-op-forged";
            File.AppendAllText(path, payload.ToJsonString() + Environment.NewLine);

            Assert.Throws<InvalidDataException>(() =>
                new ApplicationService(storagePath: path, journalIntegrityKey: journalKey));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("reorder")]
    [InlineData("rollback")]
    [InlineData("insert")]
    public async Task Operation_journal_chain_detects_reorder_rollback_and_insertion(string mutation)
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-operation-chain-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "operations.jsonl");
            var journalKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var context = DurableOperationContext();
            var service = new ApplicationService(storagePath: path, journalIntegrityKey: journalKey);
            var operation = await service.SubmitAsync(context, DurableOperationCommand(context));
            Assert.True(service.TryClaimPending(operation.OperationId, out _));
            var lines = File.ReadAllLines(path).ToList();
            Assert.Equal(2, lines.Count);
            if (mutation == "reorder") lines.Reverse();
            else if (mutation == "rollback") lines.RemoveAt(lines.Count - 1);
            else lines.Insert(1, lines[0]);
            File.WriteAllLines(path, lines);

            Assert.Throws<InvalidDataException>(() =>
                new ApplicationService(storagePath: path, journalIntegrityKey: journalKey));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Plaintext_session_journal_is_append_only_sealed_once_and_reopens_idempotently()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-session-migrate-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sourcePath = Path.Combine(root, "source.jsonl");
            var legacyPath = Path.Combine(root, "legacy.jsonl");
            var signingKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var journalKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var tokens = new SessionTokenService(signingKey);
            var context = new RuntimeRequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "session",
                AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
            var source = new FileSessionManager(tokens, sourcePath, journalIntegrityKey: journalKey);
            var pair = source.Create(context, TimeSpan.FromMinutes(5), SessionAudiences.Ui);
            Directory.CreateDirectory(root);
            File.WriteAllText(legacyPath, AuthenticatedPayload(File.ReadAllLines(sourcePath)[0]).ToJsonString() + Environment.NewLine);

            _ = new FileSessionManager(tokens, legacyPath, journalIntegrityKey: journalKey);
            var migratedLines = File.ReadAllLines(legacyPath);
            Assert.Equal(2, migratedLines.Length);
            Assert.Equal("digitalbrain.authenticated-jsonl.v1", JsonNode.Parse(migratedLines[1])!["$journal"]!.GetValue<string>());
            var reopened = new FileSessionManager(tokens, legacyPath, journalIntegrityKey: journalKey);
            Assert.Equal(migratedLines.Length, File.ReadAllLines(legacyPath).Length);
            Assert.True(reopened.TryRefresh(pair.RefreshToken, TimeSpan.FromMinutes(5), SessionAudiences.Ui, out _));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Plaintext_operation_journal_is_append_only_sealed_once_and_reopens_idempotently()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-operation-migrate-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sourcePath = Path.Combine(root, "source.jsonl");
            var legacyPath = Path.Combine(root, "legacy.jsonl");
            var journalKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var context = DurableOperationContext();
            var source = new ApplicationService(storagePath: sourcePath, journalIntegrityKey: journalKey);
            var operation = await source.SubmitAsync(context, DurableOperationCommand(context));
            Directory.CreateDirectory(root);
            File.WriteAllText(legacyPath, AuthenticatedPayload(File.ReadAllLines(sourcePath)[0]).ToJsonString() + Environment.NewLine);

            var migrated = new ApplicationService(storagePath: legacyPath, journalIntegrityKey: journalKey);
            Assert.Equal(new[] { operation.OperationId }, migrated.GetPendingOperationIds());
            var migratedLineCount = File.ReadAllLines(legacyPath).Length;
            Assert.Equal(2, migratedLineCount);
            var reopened = new ApplicationService(storagePath: legacyPath, journalIntegrityKey: journalKey);
            Assert.Equal(new[] { operation.OperationId }, reopened.GetPendingOperationIds());
            Assert.Equal(migratedLineCount, File.ReadAllLines(legacyPath).Length);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Operation_journal_repairs_only_a_provably_incomplete_final_append()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-operation-tail-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "operations.jsonl");
            var journalKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var context = DurableOperationContext();
            var service = new ApplicationService(storagePath: path, journalIntegrityKey: journalKey);
            var operation = await service.SubmitAsync(context, DurableOperationCommand(context));
            var validLength = new FileInfo(path).Length;
            const string privateTail = "{\"private-payload\":\"must-not-be-copied\"";
            File.AppendAllText(path, privateTail);

            var reopened = new ApplicationService(storagePath: path, journalIntegrityKey: journalKey);
            Assert.Equal(new[] { operation.OperationId }, reopened.GetPendingOperationIds());
            Assert.Equal(validLength, new FileInfo(path).Length);
            var quarantine = File.ReadAllText(path + ".quarantine");
            Assert.Contains("incomplete-final-append", quarantine, StringComparison.Ordinal);
            Assert.Contains("sha256", quarantine, StringComparison.Ordinal);
            Assert.DoesNotContain("private-payload", quarantine, StringComparison.Ordinal);
            Assert.DoesNotContain("must-not-be-copied", quarantine, StringComparison.Ordinal);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Operation_journal_still_fails_closed_on_a_malformed_interior_record()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-operation-interior-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "operations.jsonl");
            var journalKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var context = DurableOperationContext();
            var service = new ApplicationService(storagePath: path, journalIntegrityKey: journalKey);
            var operation = await service.SubmitAsync(context, DurableOperationCommand(context));
            Assert.True(service.TryClaimPending(operation.OperationId, out _));
            var lines = File.ReadAllLines(path).ToList();
            lines.Insert(1, "{interior-private-marker");
            File.WriteAllLines(path, lines);

            Assert.Throws<InvalidDataException>(() =>
                new ApplicationService(storagePath: path, journalIntegrityKey: journalKey));
            var quarantine = File.ReadAllText(path + ".quarantine");
            Assert.DoesNotContain("interior-private-marker", quarantine, StringComparison.Ordinal);
            Assert.Contains("sha256", quarantine, StringComparison.Ordinal);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Durable_journals_require_an_explicit_integrity_key()
    {
        var path = Path.Combine(Path.GetTempPath(), "v2-journal-key-" + Guid.NewGuid().ToString("N"), "operations.jsonl");
        Assert.Throws<ArgumentException>(() => new ApplicationService(storagePath: path));
        Assert.Throws<ArgumentException>(() => new FileSessionManager(
            new SessionTokenService(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            path + ".sessions"));
    }

    [Fact]
    public void Session_validation_fails_closed_for_out_of_range_expiry()
    {
        var tokens = new SessionTokenService(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        Assert.False(tokens.TryValidate("v2.s.t.u.0.0.999999999999999999.00", out _));
    }

    private static JsonObject AuthenticatedPayload(string envelopeLine) =>
        JsonNode.Parse(envelopeLine)!.AsObject()["payload"]!.AsObject();

    private static RuntimeRequestContext DurableOperationContext() =>
        new(new("t"), new("w"), new("u", PrincipalKind.User), "session", AuthAssurance.Password, "correlation",
            "idempotency", new HashSet<string> { "brain.act", "brain.read" });

    private static CommandEnvelope DurableOperationCommand(RuntimeRequestContext context) =>
        new("connector.send", 2, "command", context, JsonSerializer.SerializeToElement(new { value = "safe" }));

    private sealed class FakeContextAssembler : IContextAssembler
    {
        public Task<ConversationContext> AssembleAsync(ConversationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConversationContext(request.Context.TenantId, request.Context.WorkspaceId, request.ConversationId, Array.Empty<string>()));
    }
    private sealed class FakePlanner : IIntentCapabilityPlanner
    {
        public Task<IReadOnlyList<ToolInvocation>> PlanAsync(ConversationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ToolInvocation>>(new[] { new ToolInvocation("read", System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone()) });
    }
    private sealed class FakeModelRouter : IModelRouter
    {
        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new ModelResponse("model", "fake", true));
    }
    private sealed class FakeToolCatalog : IAuthorizedToolCatalog
    {
        public Task<ToolOutcome> InvokeAsync(RuntimeRequestContext context, ToolInvocation invocation, CancellationToken cancellationToken = default) => Task.FromResult(new ToolOutcome(ToolOutcomeKind.Success));
    }
    private sealed class FakeComposer : IResponseSurfaceComposer
    {
        public Task<string> ComposeAsync(RuntimeRequestContext context, ModelResponse response, IReadOnlyList<ToolOutcome> toolOutcomes, CancellationToken cancellationToken = default) => Task.FromResult($"{response.Text}:{toolOutcomes[0].Kind}");
    }
}
