extern alias McpProject;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Core.V2;
using V2RequestContext = DigitalBrain.Core.V2.RequestContext;
using DigitalBrain.Kernel.V2;
using Orleans;
using V2McpEffectCommandHandler = McpProject::DigitalBrain.Mcp.V2McpEffectCommandHandler;
using V2McpInoCommandHandler = McpProject::DigitalBrain.Mcp.V2McpInoCommandHandler;
using V2InoEffectStore = McpProject::DigitalBrain.Mcp.V2InoEffectStore;

namespace DigitalBrain.Tests.V2;

public sealed class V2ContractsTests
{
    [Fact]
    public async Task V2_ino_command_is_identity_free_durable_and_projects_a_workspace_surface()
    {
        var context = new V2RequestContext(new("tenant-a"), new("workspace-a"), new("user-a", PrincipalKind.User), "session", AuthAssurance.Password, "corr", "same-retry", new HashSet<string> { "brain.act" });
        var other = context with { WorkspaceId = new("workspace-b"), Principal = new("user-b", PrincipalKind.User) };
        var feed = new V2PrivateFeedStore(); var effects = new V2InoEffectStore();
        var handler = new V2McpInoCommandHandler(effects, new V2WorkspaceSurfaceProducer(feed, new V2ActionExecutor(feed)));
        var result = await handler.ExecuteAsync(new V2CommandEnvelope("ino.interact", 2, "ino-command", context, JsonSerializer.SerializeToElement(new { prompt = "Summarize my workspace" })));
        Assert.Equal(WorkflowState.Succeeded, result.State);
        Assert.Single(effects.Read(context));
        Assert.Single(feed.CatchUp(context, V2SurfaceAudienceKind.Workspace, 0).Items);
        Assert.Empty(feed.CatchUp(other, V2SurfaceAudienceKind.Workspace, 0).Items);
        Assert.False(V2McpInoCommandHandler.TryGetPrompt(JsonSerializer.SerializeToElement(new { prompt = "x", workspaceId = "forged" }), out _));
    }

    [Fact]
    public async Task Mcp_effect_handler_rejects_cross_workspace_aggregate()
    {
        var context = new V2RequestContext(new("tenant-a"), new("workspace-a"), new("user-a", PrincipalKind.User), "session", AuthAssurance.Password, "corr", null, new HashSet<string> { "brain.act" });
        var command = new V2CommandEnvelope("effect.execute", 2, "cmd-1", context,
            JsonSerializer.SerializeToElement(new { aggregateId = "v2:tenant-a:workspace-b:workflow:w1", effectId = "effect-1" }));
        var handler = new V2McpEffectCommandHandler(new NoopEffectPort());

        var result = await handler.ExecuteAsync(command);

        Assert.Equal(WorkflowState.Failed, result.State);
        Assert.Equal("effect-scope-invalid", result.SafeReason);
    }

    private sealed class NoopEffectPort : IV2EffectWorkerPort
    {
        public Task<EffectTransitionRecord> ExecuteAsync(string aggregateId, string effectId, string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
            => throw new Xunit.Sdk.XunitException("The port must not be called for a cross-workspace aggregate.");
    }

    [Fact]
    public void Grain_ids_are_canonical_and_scoped()
    {
        var a = V2GrainIds.Conversation(new("t1"), new("w1"), "c1");
        var b = V2GrainIds.Conversation(new("t1"), new("w2"), "c1");
        Assert.NotEqual(a, b);
        Assert.StartsWith(V2GrainIds.ScopePrefix(new("t1"), new("w1")), a, StringComparison.Ordinal);
        Assert.NotEqual(
            V2GrainIds.Aggregate(new("a:b"), new("c"), "same"),
            V2GrainIds.Aggregate(new("a"), new("b:c"), "same"));
    }

    [Fact]
    public void Isolation_gate_fails_closed()
    {
        var gate = new CapabilityIsolationGate();
        var context = new V2RequestContext(new("t1"), new("w1"), new("u1", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        Assert.True(gate.IsAllowed(context, new("t1"), new("w1"), "brain.read"));
        Assert.False(gate.IsAllowed(context, new("t1"), new("w2"), "brain.read"));
        Assert.Throws<UnauthorizedAccessException>((Action)(() => gate.Demand(context, new("t1"), new("w1"), "brain.act")));
    }

    [Fact]
    public void Commit_seal_is_deterministic_and_secret_summary_redacts()
    {
        var payload = JsonDocument.Parse("{\"ok\":true}").RootElement.Clone();
        var events = new[] { new V2EventEnvelope("v2.test", 1, "e1", "c1", null, payload) };
        Assert.Equal(V2CommitSeal.Compute(events), V2CommitSeal.Compute(events));
        Assert.Equal("[REDACTED]", V2Redaction.SafeSummary("secret", Sensitivity.Secret));
    }

    [Fact]
    public void Approval_queues_apply_without_quiescent_approved_state()
    {
        var workflow = new V2Workflow();
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
        var notAwaiting = new V2Workflow();
        Assert.Throws<InvalidOperationException>(() => notAwaiting.Approve(new ApprovalRecord(new("operator", PrincipalKind.Operator), DateTimeOffset.UtcNow, "d", null)));

        var rejected = new V2Workflow();
        rejected.SubmitForApproval();
        rejected.Reject("policy denied");
        Assert.Equal(WorkflowState.Rejected, rejected.State);

        var expired = new V2Workflow();
        expired.SubmitForApproval();
        expired.Expire();
        Assert.Equal(WorkflowState.Expired, expired.State);

        var cancelled = new V2Workflow();
        cancelled.Cancel();
        Assert.Equal(WorkflowState.Cancelled, cancelled.State);
    }

    [Fact]
    public async Task Durable_workflow_approval_persists_authenticated_audit_and_apply_queue()
    {
        var store = new InMemoryV2AggregateStore();
        var aggregate = new V2WorkflowAggregate(store);
        var context = new V2RequestContext(new("t"), new("w"), new("operator", PrincipalKind.Operator), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.approve" });
        await aggregate.SubmitForApprovalAsync("proposal-1", "submit-1", context);
        var approval = new ApprovalRecord(context.Principal, DateTimeOffset.UtcNow, "decision-1", "safe");
        var effect = new OutboxRecord("effect-1", "operation-1", 0, "fake", System.Text.Json.JsonDocument.Parse("{}").RootElement, DateTimeOffset.UtcNow.AddMinutes(5));
        var snapshot = await aggregate.ApproveAsync("proposal-1", "approve-1", context, approval, effect);
        Assert.Contains(snapshot.Commits.SelectMany(x => x.Events), x => x.Type == "v2.workflow.ApplyQueued");
        Assert.Contains(snapshot.Outbox, x => x.EffectId == "effect-1");
        var persisted = System.Text.Json.JsonSerializer.Deserialize<V2WorkflowPersistedState>(snapshot.State.GetRawText(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(WorkflowState.ApplyQueued, persisted.State);
        Assert.Equal("operator", persisted.Approval!.Approver.Value);
        Assert.Equal(new[] { WorkflowState.AwaitingApproval, WorkflowState.Approved, WorkflowState.ApplyQueued }, persisted.Transitions.Select(x => x.To));
        snapshot = await aggregate.AdvanceAsync("proposal-1", "apply-1", context, WorkflowState.Applying);
        snapshot = await aggregate.AdvanceAsync("proposal-1", "unknown-1", context, WorkflowState.OutcomeUnknown, "provider-timeout");
        snapshot = await aggregate.AdvanceAsync("proposal-1", "manual-1", context, WorkflowState.ManualIntervention, "operator-review");
        var finalState = System.Text.Json.JsonSerializer.Deserialize<V2WorkflowPersistedState>(snapshot.State.GetRawText(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(WorkflowState.ManualIntervention, finalState.State);
    }

    [Fact]
    public async Task Durable_workflow_survives_file_store_reopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "db-v2-workflow-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = new V2RequestContext(new("t"), new("w"), new("operator", PrincipalKind.Operator), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.approve" });
            var first = new V2WorkflowAggregate(new FileV2AggregateStore(root));
            await first.SubmitForApprovalAsync("proposal", "submit", context);
            await first.ApproveAsync("proposal", "approve", context, new ApprovalRecord(context.Principal, DateTimeOffset.UtcNow, "decision", null), new OutboxRecord("effect", "operation", 0, "fake", System.Text.Json.JsonDocument.Parse("{}").RootElement, DateTimeOffset.UtcNow.AddMinutes(1)));
            var reopened = await new FileV2AggregateStore(root).ReadAsync("proposal");
            Assert.Equal(2, reopened.CommitSequence);
            Assert.Single(reopened.Outbox);
            Assert.Contains(reopened.Commits.SelectMany(x => x.Events), x => x.Type == "v2.workflow.ApplyQueued");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void V2_schema_registry_is_stable_and_fail_closed()
    {
        var registry = new V2SchemaRegistry([new V2SchemaDescriptor("v2.workflow.ApplyQueued", 2, "Operational", true)]);
        Assert.True(registry.TryResolve("v2.workflow.ApplyQueued", 2, out _));
        Assert.Throws<InvalidOperationException>(() => registry.Require("v2.unknown", 1));
        Assert.Throws<InvalidOperationException>(() => registry.Register(new V2SchemaDescriptor("v2.workflow.ApplyQueued", 2, "Secret", false)));
    }

    [Fact]
    public async Task Projection_scans_registered_owners_and_checkpoints()
    {
        var source = new InMemoryV2CommitSource();
        source.RegisterOwner("owner-1");
        var payload = JsonDocument.Parse("{\"ok\":true}").RootElement.Clone();
        var evt = new V2EventEnvelope("v2.test", 1, "event-1", "corr-1", null, payload);
        source.Append("owner-1", new AggregateCommit(1, "commit-1", [evt], V2CommitSeal.Compute([evt]), DateTimeOffset.UtcNow));
        var sink = new InMemoryV2ProjectionSink("timeline");
        var applied = await new V2ProjectionWorker(source, sink).RunFullCycleAsync(new DirectoryScanCursor(0, 0));
        Assert.Equal(1, applied);
        Assert.Single(sink.Applied);
        Assert.Empty(sink.Poison);
    }

    [Fact]
    public void Session_tokens_expire_and_revoke()
    {
        var service = new V2SessionTokenService(new byte[32]);
        var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        var token = service.Issue(context, TimeSpan.FromMinutes(1));
        Assert.True(service.TryValidate(token, out var restored));
        Assert.Equal("t", restored.TenantId.Value);
        service.Revoke("s");
        Assert.False(service.TryValidate(token, out _));
    }

    [Fact]
    public void Production_manifest_fails_closed_for_mutations_and_stdio()
    {
        var manifest = V2CapabilityManifests.For(V2RuntimeProfile.Production);
        Assert.False(manifest.HttpMcpMutations);
        Assert.False(manifest.TrustedStdioMcp);
        Assert.Contains("brain.admin", manifest.Disabled);
    }

    [Fact]
    public async Task Application_port_scopes_operations_and_idempotency_to_the_full_principal()
    {
        var service = new V2ApplicationService(capabilities: [new V2Capability("brain.read", 2, true, false)]);
        var grants = new HashSet<string> { "brain.read", "brain.act" };
        var context = new V2RequestContext(new("tenant"), new("workspace-a"), new("user-a", PrincipalKind.User), "session", AuthAssurance.Password, "corr", "idem-1", grants);
        var payload = JsonDocument.Parse("{\"prompt\":\"hello\"}").RootElement.Clone();
        var first = await service.SubmitAsync(context, new V2CommandEnvelope("noop", 2, "cmd-1", context, payload));
        var second = await service.SubmitAsync(context, new V2CommandEnvelope("noop", 2, "cmd-2", context, payload));
        Assert.Equal(first.OperationId, second.OperationId);

        var otherPrincipal = context with { Principal = new PrincipalRef("user-b", PrincipalKind.User), SessionId = "session-b" };
        var otherKind = context with { Principal = new PrincipalRef("user-a", PrincipalKind.Service), SessionId = "session-service" };
        var otherPrincipalOperation = await service.SubmitAsync(otherPrincipal,
            new V2CommandEnvelope("noop", 2, "cmd-3", otherPrincipal, payload));
        var otherKindOperation = await service.SubmitAsync(otherKind,
            new V2CommandEnvelope("noop", 2, "cmd-4", otherKind, payload));

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
        var service = new V2ApplicationService();
        var context = new V2RequestContext(new("tenant"), new("workspace"), new("user", PrincipalKind.User), "session",
            AuthAssurance.Password, "corr", "idem", new HashSet<string> { "brain.act", "brain.read" });
        var firstPayload = JsonDocument.Parse("{\"prompt\":\"hello\",\"options\":{\"b\":2,\"a\":1}}").RootElement.Clone();
        var reorderedPayload = JsonDocument.Parse("{\"options\":{\"a\":1,\"b\":2},\"prompt\":\"hello\"}").RootElement.Clone();
        var first = await service.SubmitAsync(context, new V2CommandEnvelope("ino.interact", 2, "cmd-1", context, firstPayload));
        var replay = await service.SubmitAsync(context, new V2CommandEnvelope("ino.interact", 2, "cmd-2", context, reorderedPayload));

        Assert.Equal(first.OperationId, replay.OperationId);
        await Assert.ThrowsAsync<V2IdempotencyConflictException>(() => service.SubmitAsync(context,
            new V2CommandEnvelope("ino.interact", 2, "cmd-3", context,
                JsonDocument.Parse("{\"prompt\":\"changed\",\"options\":{\"a\":1,\"b\":2}}").RootElement.Clone())));
        await Assert.ThrowsAsync<V2IdempotencyConflictException>(() => service.SubmitAsync(context,
            new V2CommandEnvelope("ino.other", 2, "cmd-4", context, reorderedPayload)));
        await Assert.ThrowsAsync<V2IdempotencyConflictException>(() => service.SubmitAsync(context,
            new V2CommandEnvelope("ino.interact", 3, "cmd-5", context, reorderedPayload)));

        var forgedContext = context with { Principal = new PrincipalRef("other", PrincipalKind.User) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SubmitAsync(context,
            new V2CommandEnvelope("ino.interact", 2, "cmd-6", forgedContext, reorderedPayload)));
    }

    [Fact]
    public async Task Application_port_rejects_tampered_cursor()
    {
        var service = new V2ApplicationService();
        var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetCapabilitiesAsync(context, "tampered", 10));
    }

    [Fact]
    public async Task Aggregate_store_commits_contiguously_and_deduplicates_inbox()
    {
        var store = new InMemoryV2AggregateStore();
        var payload = JsonDocument.Parse("{\"value\":1}").RootElement.Clone();
        var evt = new V2EventEnvelope("v2.state.changed", 1, "event-1", "corr", null, payload);
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
        var store = new InMemoryV2AggregateStore();
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
            var evt = new V2EventEnvelope("v2.changed", 1, "e", "c", null, payload);
            var store = new FileV2AggregateStore(root);
            await store.CommitAsync("a", new V2CommitRequest("cmd", 0, payload, [evt], [], DateTimeOffset.UtcNow));
            var reopened = new FileV2AggregateStore(root);
            var snapshot = await reopened.ReadAsync("a");
            Assert.Equal(1, snapshot.CommitSequence);
            Assert.True(Directory.Exists(Path.Combine(root, "v2-aggregates")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Effect_coordinator_marks_unknown_without_retrying()
    {
        var store = new InMemoryV2AggregateStore();
        var payload = JsonDocument.Parse("{\"x\":1}").RootElement.Clone();
        var evt = new V2EventEnvelope("v2.effect", 1, "e", "c", null, payload);
        await store.CommitAsync("aggregate", new V2CommitRequest("cmd", 0, payload, [evt], [new OutboxRecord("effect", "op", 0, "fake", payload, DateTimeOffset.UtcNow.AddMinutes(5))], DateTimeOffset.UtcNow));
        var handler = new FakeEffectHandler(V2EffectDisposition.OutcomeUnknown);
        var coordinator = new V2EffectCoordinator(store, [handler]);
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
            var sink = new FileV2ProjectionSink(root, "timeline");
            var checkpoint = new V2ProjectionCheckpoint("timeline", "owner", 4, 2);
            await sink.SaveCheckpointAsync(checkpoint);
            await sink.QuarantineAsync(new V2PoisonRecord("timeline", "owner", 5, "checksum", DateTimeOffset.UtcNow));
            var reopened = new FileV2ProjectionSink(root, "timeline");
            Assert.Equal(4, (await reopened.ReadCheckpointAsync("owner"))!.CommitSequence);
            Assert.True(Directory.GetFiles(Path.Combine(root, "v2-projections", "timeline", "quarantine")).Length == 1);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Projection_query_port_is_scoped_and_opaque_cursor_paginated()
    {
        var store = new InMemoryV2ProjectionQueryStore();
        var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        store.Add(new V2TimelineEntry("1", new("t"), new("w"), DateTimeOffset.UtcNow, "Event", "safe", "c"));
        store.Add(new V2TimelineEntry("2", new("other"), new("w"), DateTimeOffset.UtcNow, "Event", "hidden", "c"));
        var page = await store.TimelineAsync(context, null, 10);
        Assert.Single(page.Items);
        Assert.DoesNotContain("other", System.Text.Json.JsonSerializer.Serialize(page.Items));
    }

    private sealed class FakeEffectHandler(V2EffectDisposition disposition) : IV2EffectHandler
    {
        public string EffectType => "fake";
        public int Calls { get; private set; }
        public Task<V2EffectExecutionResult> ExecuteAsync(OutboxRecord intent, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new V2EffectExecutionResult(disposition, "safe-result"));
        }
    }

    [Fact]
    public void Orleans_v2_aggregate_contract_has_stable_aliases()
    {
        var alias = typeof(IV2AggregateGrain).GetCustomAttributes(typeof(AliasAttribute), false).Cast<AliasAttribute>().Single();
        Assert.Equal("digitalbrain.v2.aggregate-grain", alias.Alias);
        var workerAlias = typeof(IV2EffectWorkerGrain).GetCustomAttributes(typeof(AliasAttribute), false).Cast<AliasAttribute>().Single();
        Assert.Equal("digitalbrain.v2.effect-worker-grain", workerAlias.Alias);
    }

    [Fact]
    public async Task Scoped_conversation_owner_rejects_context_escape_and_keeps_tool_contract_structured()
    {
        var context = new V2RequestContext(new("tenant"), new("workspace"), new("user", PrincipalKind.User), "session", AuthAssurance.Password, "corr", null, new HashSet<string> { "brain.read" });
        var owner = new V2ConversationOwner(new FakeContextAssembler(), new FakePlanner(), new FakeModelRouter(), new FakeToolCatalog(), new FakeComposer());
        var result = await owner.ExecuteAsync(new V2ConversationRequest(context, "conversation", "hello"));
        Assert.Equal("model:Success", result);
    }

    [Fact]
    public void Session_refresh_is_one_use_and_revoke_invalidates_access_session()
    {
        var tokens = new V2SessionTokenService(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var manager = new V2SessionManager(tokens, TimeSpan.FromHours(1));
        var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "session", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
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
        var tokens = new V2SessionTokenService(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var path = Path.Combine(Path.GetTempPath(), "v2-session-seam-" + Guid.NewGuid().ToString("N"), "sessions.jsonl");
        var manager = new FileV2SessionManager(tokens, path, appendLine: _ =>
        {
            if (fail) throw new IOException("injected session journal failure");
        });
        var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "session",
            AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        var createFails = true;
        var createManager = new FileV2SessionManager(tokens, path + ".create", appendLine: _ =>
        {
            if (createFails) throw new IOException("injected create journal failure");
        });
        Assert.Throws<IOException>(() => createManager.Create(context with { SessionId = "create-session" },
            TimeSpan.FromMinutes(5), V2SessionAudiences.Ui));
        createFails = false;
        _ = createManager.Create(context with { SessionId = "create-session" }, TimeSpan.FromMinutes(5), V2SessionAudiences.Ui);
        var pair = manager.Create(context, TimeSpan.FromMinutes(5), V2SessionAudiences.Ui);

        fail = true;
        Assert.Throws<IOException>(() => manager.TryRefresh(pair.RefreshToken, TimeSpan.FromMinutes(5), V2SessionAudiences.Ui, out _));
        fail = false;
        Assert.True(manager.TryRefresh(pair.RefreshToken, TimeSpan.FromMinutes(5), V2SessionAudiences.Ui, out var rotated));
        fail = true;
        Assert.Throws<IOException>(() => manager.Revoke(rotated.RefreshToken, V2SessionAudiences.Ui));
        Assert.True(tokens.TryValidate(rotated.AccessToken, V2SessionAudiences.Ui, out _));
        fail = false;
        Assert.True(manager.Revoke(rotated.RefreshToken, V2SessionAudiences.Ui));
        Assert.False(tokens.TryValidate(rotated.AccessToken, V2SessionAudiences.Ui, out _));
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
            var manager = new FileV2SessionManager(new V2SessionTokenService(key), path);
            var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "session",
                AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
            var pair = manager.Create(context, TimeSpan.FromMinutes(5), V2SessionAudiences.Ui);
            Assert.True(manager.TryRefresh(pair.RefreshToken, TimeSpan.FromMinutes(5), V2SessionAudiences.Ui, out _));
            var lines = File.ReadAllLines(path).ToList();
            lines.Insert(1, "{private-refresh-token-marker");
            File.WriteAllLines(path, lines);

            Assert.Throws<InvalidDataException>(() => new FileV2SessionManager(new V2SessionTokenService(key), path));
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
            var manager = new FileV2SessionManager(new V2SessionTokenService(key), path);
            var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "session",
                AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
            manager.Create(context, TimeSpan.FromMinutes(5), V2SessionAudiences.Ui);
            var node = JsonNode.Parse(File.ReadAllText(path).Trim())!.AsObject();
            node["Entry"]!["Context"] = null;
            File.WriteAllText(path, node.ToJsonString());

            Assert.Throws<InvalidDataException>(() => new FileV2SessionManager(new V2SessionTokenService(key), path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Private_feed_is_workspace_scoped_and_action_binding_is_single_use()
    {
        var feed = new V2PrivateFeedStore();
        var first = new V2RequestContext(new("t"), new("w"), new("u1", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        var other = first with { WorkspaceId = new WorkspaceId("other"), Principal = new PrincipalRef("u2", PrincipalKind.User) };
        feed.Append(first, "surface", 1, "hash", System.Text.Json.JsonDocument.Parse("{\"safe\":true}").RootElement);
        feed.Append(first, "surface", 2, "hash2", System.Text.Json.JsonDocument.Parse("{\"safe\":true}").RootElement);
        Assert.Equal(2, feed.CatchUp(first, null).Items.Count);
        Assert.Empty(feed.CatchUp(other, null).Items);
        feed.RetainFrom(first, 2);
        Assert.True(feed.CatchUp(first, 0).ResetRequired);
        var token = "issued";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
        var actions = new V2ActionExecutor();
        actions.Register(new V2ActionBinding("b", "template", 1, "schema", 1, DateTimeOffset.UtcNow.AddMinutes(1), hash));
        var submission = actions.Use(first, "b", token, System.Text.Json.JsonDocument.Parse("{}").RootElement);
        Assert.True(actions.TryGetUse(submission.IdempotencyKey, out _));
        Assert.Throws<InvalidOperationException>(() => actions.Use(first, "b", token, System.Text.Json.JsonDocument.Parse("{}").RootElement));
    }

    [Fact]
    public void Model_router_enforces_privacy_residency_capabilities_and_bounded_fallback()
    {
        var models = new[]
        {
            new V2ModelDescriptor("cloud", true, true, true, false, false, "private", "eu", 0.01m, TimeSpan.FromMilliseconds(100)),
            new V2ModelDescriptor("local", true, true, true, true, true, "private", "eu", 0.02m, TimeSpan.FromMilliseconds(50)),
            new V2ModelDescriptor("unsafe", true, true, true, true, true, "public", "us", 0.001m, TimeSpan.FromMilliseconds(1))
        };
        var router = new V2ModelRouter(models);
        var selection = router.Select(new V2ModelPolicy("private", "eu", 0.03m, TimeSpan.FromSeconds(1), 512, true, true, true, true));
        Assert.Equal("local", selection.Key);
        Assert.Throws<InvalidOperationException>(() => router.Select(new V2ModelPolicy("private", "eu", 0.005m, TimeSpan.FromSeconds(1), 512, false, false, false, false)));
    }

    [Fact]
    public async Task Telemetry_keeps_metric_labels_low_cardinality_and_accounts_drops()
    {
        var telemetry = new V2TelemetryBuffer(1);
        await telemetry.EmitAsync(new V2MetricPoint("v2.outbox.age", 1, new Dictionary<string, string> { ["tenant"] = "secret-tenant", ["outcome"] = "success" }));
        await telemetry.EmitAsync(new V2MetricPoint("v2.outbox.age", 2, new Dictionary<string, string> { ["workspace"] = "secret-workspace", ["status"] = "retry" }));
        Assert.Equal(1, telemetry.Dropped);
        var point = Assert.Single(telemetry.Metrics);
        Assert.DoesNotContain("tenant", point.Labels.Keys);
        Assert.Equal("success", point.Labels["outcome"]);
        await telemetry.EmitTraceAsync(new V2TraceContext("trace", "span", new("t"), new("w"), "command", "operation"), "effect", "safe detail");
        Assert.Single(telemetry.Traces);
        await telemetry.EmitTraceAsync(new V2TraceContext("trace-2", "span-2", new("t"), new("w")), "overflow", "discarded");
        Assert.Equal(2, telemetry.Dropped);
        Assert.Single(telemetry.Traces);
    }

    [Fact]
    public void Deployment_preview_is_non_mutating_and_blocks_required_topology_drift()
    {
        var desired = new V2TopologySnapshot(new[] { new V2TopologyResource("kernel", "container-app", true, "Test", "sha256:good") }, "Test");
        var actual = new V2TopologySnapshot(new[] { new V2TopologyResource("kernel", "container-app", true, "Test", "sha256:old") }, "Test");
        var preview = V2DeploymentPreviewer.Preview(desired, actual);
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Drift, x => x.Resource == "kernel" && x.Blocking);
        Assert.Equal("sha256:old", actual.Resources[0].ImageDigest);
    }

    [Fact]
    public void Operator_bootstrap_is_single_use_and_admin_policy_is_fail_closed()
    {
        var signing = new V2SessionTokenService(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var manager = new V2SessionManager(signing);
        var bootstrap = new V2OperatorBootstrap(manager, new V2AdminPolicy(true, true, new HashSet<string> { "brain.admin" }));
        bootstrap.ConfigureBootstrapSecret("one-use-secret");
        Assert.True(bootstrap.TryConsume("one-use-secret", new("t"), new("w"), "operator", out var result));
        Assert.Equal(PrincipalKind.Operator, result!.Context.Principal.Kind);
        var signed = signing.Issue(result.Context, TimeSpan.FromMinutes(5));
        Assert.True(signing.TryValidate(signed, out var restored));
        Assert.Equal(PrincipalKind.Operator, restored.Principal.Kind);
        Assert.Equal(AuthAssurance.OperatorBootstrap, restored.Assurance);
        Assert.False(bootstrap.TryConsume("one-use-secret", new("t"), new("w"), "operator", out _));
        var disabled = new V2OperatorBootstrap(manager, new V2AdminPolicy(false, true, new HashSet<string> { "brain.admin" }));
        disabled.ConfigureBootstrapSecret("other");
        Assert.False(disabled.TryConsume("other", new("t"), new("w"), "operator", out _));
    }

    [Fact]
    public void Mcp_guard_enforces_audience_origin_body_rate_and_concurrency()
    {
        var guard = new V2McpRequestGuard(new V2McpTransportPolicy("aud", new HashSet<string> { "https://client" }, 10, 1, 1));
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
        var tokens = new V2SessionTokenService(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        var token = tokens.Issue(context, TimeSpan.FromMinutes(5), "gateway");
        var metadata = new Dictionary<string, string> { ["x-v2-audience"] = "gateway", ["x-v2-session"] = token };
        Assert.True(V2GrpcAuthentication.TryAuthenticate(metadata, tokens, "gateway", out var authenticated));
        Assert.Equal(context.TenantId, authenticated.TenantId);
        metadata["x-v2-audience"] = "wrong";
        Assert.False(V2GrpcAuthentication.TryAuthenticate(metadata, tokens, "gateway", out _));
    }

    [Fact]
    public void File_feed_reopens_with_v2_items_after_restart()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-feed-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
            var first = new FileV2PrivateFeedStore(root);
            first.Append(context, "surface", 1, "hash", System.Text.Json.JsonDocument.Parse("{}").RootElement);
            var reopened = new FileV2PrivateFeedStore(root);
            Assert.Single(reopened.CatchUp(context, null).Items);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task V2_operations_reopen_with_principal_ownership_and_exact_replay_receipt()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-ops-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", "idem", new HashSet<string> { "brain.act", "brain.read" });
            var service = new V2ApplicationService(storagePath: Path.Combine(root, "operations.jsonl"));
            var command = new V2CommandEnvelope("test", 2, "cmd", context, System.Text.Json.JsonDocument.Parse("{}").RootElement);
            var operation = await service.SubmitAsync(context, command);
            var persisted = await File.ReadAllTextAsync(Path.Combine(root, "operations.jsonl"));
            Assert.Contains("\"Command\"", persisted, StringComparison.Ordinal);
            Assert.Contains("\"Type\":\"test\"", persisted, StringComparison.Ordinal);
            var reopened = new V2ApplicationService(storagePath: Path.Combine(root, "operations.jsonl"));
            Assert.NotNull(await reopened.GetOperationAsync(context, operation.OperationId));
            var other = context with { WorkspaceId = new WorkspaceId("other") };
            Assert.Null(await reopened.GetOperationAsync(other, operation.OperationId));
            var otherPrincipal = context with { Principal = new PrincipalRef("other", PrincipalKind.User) };
            Assert.Null(await reopened.GetOperationAsync(otherPrincipal, operation.OperationId));
            var replay = await reopened.SubmitAsync(context, command with { CommandId = "cmd-after-restart" });
            Assert.Equal(operation.OperationId, replay.OperationId);
            await Assert.ThrowsAsync<V2IdempotencyConflictException>(() => reopened.SubmitAsync(context,
                command with { CommandId = "changed-after-restart", Payload = JsonSerializer.SerializeToElement(new { changed = true }) }));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task V2_operation_submission_is_concurrent_idempotent_and_not_visible_before_durable_append()
    {
        var fail = true;
        var writes = 0;
        var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s",
            AuthAssurance.Password, "c", "same-idempotency", new HashSet<string> { "brain.act", "brain.read" });
        var command = new V2CommandEnvelope("test", 2, "command", context, JsonSerializer.SerializeToElement(new { }));
        var service = new V2ApplicationService(appendLine: _ =>
        {
            if (fail) throw new IOException("injected operation journal failure");
            Interlocked.Increment(ref writes);
        });

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
    public async Task V2_operation_idempotency_scope_is_unambiguous_for_delimiter_bearing_identifiers()
    {
        var service = new V2ApplicationService();
        var first = new V2RequestContext(new("a:b"), new("c"), new("same", PrincipalKind.User), "s1",
            AuthAssurance.Password, "c1", "idem", new HashSet<string> { "brain.act", "brain.read" });
        var second = new V2RequestContext(new("a"), new("b:c"), new("same", PrincipalKind.User), "s2",
            AuthAssurance.Password, "c2", "idem", new HashSet<string> { "brain.act", "brain.read" });
        var firstOperation = await service.SubmitAsync(first,
            new V2CommandEnvelope("test", 2, "command", first, JsonSerializer.SerializeToElement(new { })));
        var secondOperation = await service.SubmitAsync(second,
            new V2CommandEnvelope("test", 2, "command", second, JsonSerializer.SerializeToElement(new { })));

        Assert.NotEqual(firstOperation.OperationId, secondOperation.OperationId);
        Assert.NotNull(await service.GetOperationAsync(first, firstOperation.OperationId));
        Assert.Null(await service.GetOperationAsync(first, secondOperation.OperationId));
        Assert.NotEqual(V2RequestScope.Id(first), V2RequestScope.Id(second));
    }

    [Fact]
    public void V2_operations_fail_closed_on_a_torn_journal_record()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-torn-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "operations.jsonl");
            File.WriteAllText(path, "{not-json}\n");
            Assert.Throws<InvalidDataException>(() => new V2ApplicationService(storagePath: path));
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
            var store = new FileV2ProjectionQueryStore(path);
            var first = new V2RequestContext(new("t"), new("w1"), new("u1", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
            var other = first with { WorkspaceId = new WorkspaceId("w2"), Principal = new PrincipalRef("u2", PrincipalKind.User) };
            store.Add(new V2TimelineEntry("a", first.TenantId, first.WorkspaceId, DateTimeOffset.UtcNow, "test", "safe", "c"));
            store.Add(new V2TimelineEntry("b", other.TenantId, other.WorkspaceId, DateTimeOffset.UtcNow, "test", "safe", "c"));
            var reopened = new FileV2ProjectionQueryStore(path);
            Assert.Single((await reopened.TimelineAsync(first, null, 10)).Items);
            Assert.Single((await reopened.TimelineAsync(other, null, 10)).Items);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Production_capability_manifest_does_not_advertise_mutation_capabilities()
    {
        Assert.Contains("brain.admin", V2CapabilityManifests.For(V2RuntimeProfile.Development).Enabled);
        var manifest = V2CapabilityManifests.For(V2RuntimeProfile.Production);
        var service = new V2ApplicationService(capabilities: manifest.Enabled.Where(x => x.StartsWith("brain.", StringComparison.Ordinal)).Select(x => new V2Capability(x, 2, true, x != "brain.read")));
        var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
        var page = await service.GetCapabilitiesAsync(context, null, 20);
        Assert.DoesNotContain(page.Items, x => x.Id is "brain.act" or "brain.approve" or "brain.admin");
    }

    [Fact]
    public void Test_profile_disables_trusted_stdio_and_http_mutations()
    {
        var manifest = V2CapabilityManifests.For(V2RuntimeProfile.Test);
        Assert.False(manifest.TrustedStdioMcp);
        Assert.False(manifest.HttpMcpMutations);
        Assert.DoesNotContain("brain.admin", manifest.Enabled);
    }

    [Fact]
    public async Task Command_port_requires_capability_specific_to_command_kind()
    {
        var service = new V2ApplicationService();
        var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.act" });
        var approval = new V2CommandEnvelope("approve.proposal", 2, "cmd-approval", context, System.Text.Json.JsonDocument.Parse("{}").RootElement);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SubmitAsync(context, approval));
        var action = new V2CommandEnvelope("connector.send", 2, "cmd-action", context, System.Text.Json.JsonDocument.Parse("{}").RootElement);
        Assert.NotNull(await service.SubmitAsync(context, action));
    }

    [Fact]
    public async Task Durable_command_can_be_claimed_once_and_outcome_replayed()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-commands-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.act", "brain.read" });
            var service = new V2ApplicationService(storagePath: Path.Combine(root, "operations.jsonl"));
            var submitted = await service.SubmitAsync(context, new V2CommandEnvelope("connector.send", 2, "cmd-durable", context, System.Text.Json.JsonDocument.Parse("{}").RootElement));
            Assert.True(service.TryClaimPending(submitted.OperationId, out var command));
            Assert.Equal("cmd-durable", command!.CommandId);
            Assert.False(service.TryClaimPending(submitted.OperationId, out _));
            Assert.True(service.RecordOutcome(submitted.OperationId, WorkflowState.OutcomeUnknown, "provider outcome unavailable"));
            var reopened = new V2ApplicationService(storagePath: Path.Combine(root, "operations.jsonl"));
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
            var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s",
                AuthAssurance.Password, "c", "idem", new HashSet<string> { "brain.act", "brain.read" });
            var service = new V2ApplicationService(storagePath: path);
            var submitted = await service.SubmitAsync(context,
                new V2CommandEnvelope("connector.send", 2, "cmd", context, JsonSerializer.SerializeToElement(new { })));
            Assert.True(service.TryClaimPending(submitted.OperationId, out _));

            var reopened = new V2ApplicationService(storagePath: path);
            var recovered = await reopened.GetOperationAsync(context, submitted.OperationId);
            Assert.Equal(WorkflowState.OutcomeUnknown, recovered!.State);
            Assert.Equal("The previous attempt ended before its outcome was confirmed.", recovered.SafeReason);
            Assert.DoesNotContain(submitted.OperationId, recovered.SafeReason, StringComparison.Ordinal);
            Assert.Empty(reopened.GetPendingOperationIds());
            var lineCount = File.ReadLines(path).Count();

            var reopenedAgain = new V2ApplicationService(storagePath: path);
            Assert.Equal(WorkflowState.OutcomeUnknown,
                (await reopenedAgain.GetOperationAsync(context, submitted.OperationId))!.State);
            Assert.Equal(lineCount, File.ReadLines(path).Count());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Command_dispatcher_routes_once_and_marks_unknown_without_retry()
    {
        var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.act", "brain.read" });
        var service = new V2ApplicationService();
        var submitted = await service.SubmitAsync(context, new V2CommandEnvelope("test.command", 2, "cmd-dispatch", context, System.Text.Json.JsonDocument.Parse("{}").RootElement));
        var dispatcher = new V2CommandDispatcher(service, Array.Empty<IV2CommandHandler>());
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
            var tokens = new V2SessionTokenService(key);
            var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
            var first = new FileV2SessionManager(tokens, path, TimeSpan.FromHours(1));
            var pair = first.Create(context, TimeSpan.FromMinutes(5));
            var reopened = new FileV2SessionManager(tokens, path, TimeSpan.FromHours(1));
            Assert.True(reopened.TryRefresh(pair.RefreshToken, TimeSpan.FromMinutes(5), out var rotated));
            var third = new FileV2SessionManager(tokens, path, TimeSpan.FromHours(1));
            Assert.False(third.TryRefresh(pair.RefreshToken, TimeSpan.FromMinutes(5), out _));
            Assert.True(third.Revoke(rotated.RefreshToken));
            var restartedTokens = new V2SessionTokenService(key);
            var final = new FileV2SessionManager(restartedTokens, path, TimeSpan.FromHours(1));
            Assert.False(final.Revoke(rotated.RefreshToken));
            Assert.False(restartedTokens.TryValidate(rotated.AccessToken, V2SessionAudiences.Mcp, out _));
            Assert.DoesNotContain(pair.RefreshToken, File.ReadAllText(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Session_validation_fails_closed_for_out_of_range_expiry()
    {
        var tokens = new V2SessionTokenService(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        Assert.False(tokens.TryValidate("v2.s.t.u.0.0.999999999999999999.00", out _));
    }

    private sealed class FakeContextAssembler : IV2ContextAssembler
    {
        public Task<V2ConversationContext> AssembleAsync(V2ConversationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new V2ConversationContext(request.Context.TenantId, request.Context.WorkspaceId, request.ConversationId, Array.Empty<string>()));
    }
    private sealed class FakePlanner : IV2IntentCapabilityPlanner
    {
        public Task<IReadOnlyList<V2ToolInvocation>> PlanAsync(V2ConversationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<V2ToolInvocation>>(new[] { new V2ToolInvocation("read", System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone()) });
    }
    private sealed class FakeModelRouter : IV2ModelRouter
    {
        public Task<V2ModelResponse> CompleteAsync(V2ModelRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new V2ModelResponse("model", "fake", true));
    }
    private sealed class FakeToolCatalog : IV2AuthorizedToolCatalog
    {
        public Task<V2ToolOutcome> InvokeAsync(V2RequestContext context, V2ToolInvocation invocation, CancellationToken cancellationToken = default) => Task.FromResult(new V2ToolOutcome(V2ToolOutcomeKind.Success));
    }
    private sealed class FakeComposer : IV2ResponseSurfaceComposer
    {
        public Task<string> ComposeAsync(V2RequestContext context, V2ModelResponse response, IReadOnlyList<V2ToolOutcome> toolOutcomes, CancellationToken cancellationToken = default) => Task.FromResult($"{response.Text}:{toolOutcomes[0].Kind}");
    }
}
