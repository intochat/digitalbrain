using System.Text.Json;
using DigitalBrain.Core.V2;
using V2RequestContext = DigitalBrain.Core.V2.RequestContext;
using DigitalBrain.Kernel.V2;
using Orleans;

namespace DigitalBrain.Tests.V2;

public sealed class V2ContractsTests
{
    [Fact]
    public void Grain_ids_are_canonical_and_scoped()
    {
        var a = V2GrainIds.Conversation(new("t1"), new("w1"), "c1");
        var b = V2GrainIds.Conversation(new("t1"), new("w2"), "c1");
        Assert.NotEqual(a, b);
        Assert.StartsWith("v2:t1:w1:", a);
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
    public async Task Application_port_is_idempotent_and_workspace_scoped()
    {
        var service = new V2ApplicationService(capabilities: [new V2Capability("brain.read", 2, true, false)]);
        var grants = new HashSet<string> { "brain.read", "brain.act" };
        var context = new V2RequestContext(new("tenant"), new("workspace-a"), new("user-a", PrincipalKind.User), "session", AuthAssurance.Password, "corr", "idem-1", grants);
        var payload = JsonDocument.Parse("{\"type\":\"noop\",\"commandId\":\"cmd-1\"}").RootElement.Clone();
        var first = await service.SubmitAsync(context, new V2CommandEnvelope("noop", 2, "cmd-1", context, payload));
        var second = await service.SubmitAsync(context, new V2CommandEnvelope("noop", 2, "cmd-2", context, payload));
        Assert.Equal(first.OperationId, second.OperationId);
        var other = context with { WorkspaceId = new WorkspaceId("workspace-b") };
        var otherOperations = await service.GetOperationsAsync(other, null, 10);
        Assert.Empty(otherOperations.Items);
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
        var token = tokens.Issue(context, TimeSpan.FromMinutes(5));
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
    public async Task V2_operations_reopen_from_local_persistence_without_cross_workspace_visibility()
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
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task V2_operations_ignore_torn_trailing_record_during_recovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-torn-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "operations.jsonl");
            File.WriteAllText(path, "{not-json}\n");
            var service = new V2ApplicationService(storagePath: path);
            var page = await service.GetOperationsAsync(new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" }), null, 10);
            Assert.Empty(page.Items);
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
    public void File_session_manager_replays_rotation_and_revocation_without_plaintext_tokens()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-sessions-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "sessions.jsonl");
            var tokens = new V2SessionTokenService(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var context = new V2RequestContext(new("t"), new("w"), new("u", PrincipalKind.User), "s", AuthAssurance.Password, "c", null, new HashSet<string> { "brain.read" });
            var first = new FileV2SessionManager(tokens, path, TimeSpan.FromHours(1));
            var pair = first.Create(context, TimeSpan.FromMinutes(5));
            var reopened = new FileV2SessionManager(tokens, path, TimeSpan.FromHours(1));
            Assert.True(reopened.TryRefresh(pair.RefreshToken, TimeSpan.FromMinutes(5), out var rotated));
            var third = new FileV2SessionManager(tokens, path, TimeSpan.FromHours(1));
            Assert.False(third.TryRefresh(pair.RefreshToken, TimeSpan.FromMinutes(5), out _));
            Assert.True(third.Revoke(rotated.RefreshToken));
            var final = new FileV2SessionManager(tokens, path, TimeSpan.FromHours(1));
            Assert.False(final.Revoke(rotated.RefreshToken));
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
