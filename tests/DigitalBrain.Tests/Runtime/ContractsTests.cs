using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Core.V2;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Tests.Runtime;

public sealed class ContractsTests
{
    [Fact]
    public void Grain_ids_are_canonical_and_scoped()
    {
        var first = GrainIds.Conversation(new("tenant"), new("workspace-a"), "conversation");
        var second = GrainIds.Conversation(new("tenant"), new("workspace-b"), "conversation");

        Assert.NotEqual(first, second);
        Assert.StartsWith(GrainIds.ScopePrefix(new("tenant"), new("workspace-a")), first, StringComparison.Ordinal);
        Assert.NotEqual(
            GrainIds.Aggregate(new("a:b"), new("c"), "same"),
            GrainIds.Aggregate(new("a"), new("b:c"), "same"));
    }

    [Fact]
    public void Isolation_gate_fails_closed()
    {
        var gate = new CapabilityIsolationGate();
        var context = Context("brain.read");

        Assert.True(gate.IsAllowed(context, new("tenant"), new("workspace"), "brain.read"));
        Assert.False(gate.IsAllowed(context, new("tenant"), new("other-workspace"), "brain.read"));
        Assert.Throws<UnauthorizedAccessException>(
            () => gate.Demand(context, new("tenant"), new("workspace"), "brain.act"));
    }

    [Fact]
    public void Commit_seal_is_deterministic_and_secret_summary_redacts()
    {
        var payload = JsonElement.Parse("""{"ok":true}""");
        var events = new[] { new EventEnvelope("v2.test", 1, "event", "correlation", null, payload) };

        Assert.Equal(CommitSeal.Compute(events), CommitSeal.Compute(events));
        Assert.Equal("[REDACTED]", Redaction.SafeSummary("secret", Sensitivity.Secret));
    }

    [Fact]
    public void Workflow_approval_queues_apply_and_terminal_guards_fail_closed()
    {
        var workflow = new Workflow();
        workflow.SubmitForApproval();
        workflow.Approve(new ApprovalRecord(
            new("operator", PrincipalKind.Operator),
            DateTimeOffset.UtcNow,
            "decision",
            null));

        Assert.Equal(WorkflowState.ApplyQueued, workflow.State);
        Assert.Equal(
            [WorkflowState.AwaitingApproval, WorkflowState.Approved, WorkflowState.ApplyQueued],
            workflow.Transitions.Select(transition => transition.To));

        var rejected = new Workflow();
        rejected.SubmitForApproval();
        rejected.Reject("policy denied");
        Assert.Equal(WorkflowState.Rejected, rejected.State);
        Assert.Throws<InvalidOperationException>(() => rejected.BeginApply());

        var expired = new Workflow();
        expired.SubmitForApproval();
        expired.Expire();
        Assert.Equal(WorkflowState.Expired, expired.State);
    }

    [Fact]
    public async Task Durable_workflow_approval_persists_audit_and_apply_queue()
    {
        var store = new InMemoryAggregateStore();
        var aggregate = new WorkflowAggregate(store);
        var context = Context("brain.approve") with
        {
            Principal = new("operator", PrincipalKind.Operator)
        };
        await aggregate.SubmitForApprovalAsync("proposal", "submit", context);
        var effect = new OutboxRecord(
            "effect",
            "operation",
            0,
            "fake",
            JsonElement.Parse("{}"),
            DateTimeOffset.UtcNow.AddMinutes(5));

        var snapshot = await aggregate.ApproveAsync(
            "proposal",
            "approve",
            context,
            new ApprovalRecord(context.Principal, DateTimeOffset.UtcNow, "decision", "safe"),
            effect);

        Assert.Contains(snapshot.Commits.SelectMany(commit => commit.Events),
            item => item.Type == "v2.workflow.ApplyQueued");
        Assert.Contains(snapshot.Outbox, item => item.EffectId == "effect");
        var persisted = JsonSerializer.Deserialize<WorkflowPersistedState>(
            snapshot.State.GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(WorkflowState.ApplyQueued, persisted!.State);
        Assert.Equal("operator", persisted.Approval!.Approver.Value);
    }

    [Fact]
    public void Schema_registry_is_stable_and_fail_closed()
    {
        var registry = new SchemaRegistry([
            new SchemaDescriptor("v2.workflow.ApplyQueued", 2, "Operational", true)
        ]);

        Assert.True(registry.TryResolve("v2.workflow.ApplyQueued", 2, out _));
        Assert.Throws<InvalidOperationException>(() => registry.Require("v2.unknown", 1));
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new SchemaDescriptor("v2.workflow.ApplyQueued", 2, "Secret", false)));
    }

    [Fact]
    public async Task Aggregate_store_commits_contiguously_and_deduplicates_inbox()
    {
        var store = new InMemoryAggregateStore();
        var payload = JsonElement.Parse("""{"value":1}""");
        var item = new EventEnvelope("v2.state.changed", 1, "event", "correlation", null, payload);
        var request = new V2CommitRequest(
            "command",
            0,
            payload,
            [item],
            [new OutboxRecord(
                "effect",
                "operation",
                0,
                "fake",
                payload,
                DateTimeOffset.UtcNow.AddMinutes(1))],
            DateTimeOffset.UtcNow);

        var first = await store.CommitAsync("aggregate", request);
        var duplicate = await store.CommitAsync("aggregate", request);

        Assert.True(first.Accepted);
        Assert.True(duplicate.Duplicate);
        Assert.Equal(first.Commit.CommitId, duplicate.Commit.CommitId);
        Assert.Single(duplicate.Snapshot.Outbox);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitAsync(
            "aggregate",
            request with { CommandId = "different-command" }));
    }

    [Fact]
    public async Task Effect_transition_history_is_append_only_and_idempotent()
    {
        var store = new InMemoryAggregateStore();
        var transition = new EffectTransitionRecord(
            "effect",
            "transition",
            "Applying",
            "safe",
            DateTimeOffset.UtcNow);

        await store.AppendEffectTransitionAsync("aggregate", transition);
        await store.AppendEffectTransitionAsync("aggregate", transition);

        Assert.Single((await store.ReadAsync("aggregate")).EffectTransitions);
    }

    [Fact]
    public async Task Effect_coordinator_marks_unknown_without_retrying()
    {
        var store = new InMemoryAggregateStore();
        var payload = JsonElement.Parse("""{"value":1}""");
        await store.CommitAsync("aggregate", new V2CommitRequest(
            "command",
            0,
            payload,
            [],
            [new OutboxRecord(
                "effect",
                "operation",
                0,
                "fake",
                payload,
                DateTimeOffset.UtcNow.AddMinutes(5))],
            DateTimeOffset.UtcNow));
        var handler = new FakeEffectHandler(EffectDisposition.OutcomeUnknown);

        var result = await new EffectCoordinator(store, [handler])
            .ExecuteOnceAsync("aggregate", "effect", "worker", TimeSpan.FromMinutes(1));

        Assert.Equal("OutcomeUnknown", result.State);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void Model_router_enforces_privacy_residency_capabilities_and_bounded_fallback()
    {
        var router = new ModelRouter([
            new ModelDescriptor("cloud", true, true, true, false, false, "private", "eu", 0.01m,
                TimeSpan.FromMilliseconds(100)),
            new ModelDescriptor("local", true, true, true, true, true, "private", "eu", 0.02m,
                TimeSpan.FromMilliseconds(50)),
            new ModelDescriptor("unsafe", true, true, true, true, true, "public", "us", 0.001m,
                TimeSpan.FromMilliseconds(1))
        ]);

        var selection = router.Select(new ModelPolicy(
            "private",
            "eu",
            0.03m,
            TimeSpan.FromSeconds(1),
            512,
            true,
            true,
            true,
            true));

        Assert.Equal("local", selection.Key);
        Assert.Throws<InvalidOperationException>(() => router.Select(new ModelPolicy(
            "private",
            "eu",
            0.005m,
            TimeSpan.FromSeconds(1),
            512,
            false,
            false,
            false,
            false)));
    }

    [Fact]
    public async Task Telemetry_keeps_metric_labels_low_cardinality_and_accounts_drops()
    {
        var telemetry = new TelemetryBuffer(1);
        await telemetry.EmitAsync(new MetricPoint(
            "v2.outbox.age",
            1,
            new Dictionary<string, string> { ["tenant"] = "private-tenant", ["outcome"] = "success" }));
        await telemetry.EmitAsync(new MetricPoint(
            "v2.outbox.age",
            2,
            new Dictionary<string, string> { ["workspace"] = "private-workspace", ["status"] = "retry" }));

        var point = Assert.Single(telemetry.Metrics);
        Assert.DoesNotContain("tenant", point.Labels.Keys);
        Assert.Equal("success", point.Labels["outcome"]);
        Assert.Equal(1, telemetry.Dropped);
    }

    [Fact]
    public void Deployment_preview_is_non_mutating_and_blocks_required_topology_drift()
    {
        var desired = new TopologySnapshot(
            [new TopologyResource("kernel", "container-app", true, "Test", "sha256:good")],
            "Test");
        var actual = new TopologySnapshot(
            [new TopologyResource("kernel", "container-app", true, "Test", "sha256:old")],
            "Test");

        var preview = DeploymentPreviewer.Preview(desired, actual);

        Assert.False(preview.CanApply);
        Assert.Contains(preview.Drift, item => item.Resource == "kernel" && item.Blocking);
        Assert.Equal("sha256:old", actual.Resources[0].ImageDigest);
    }

    private static RuntimeRequestContext Context(params string[] grants) => new(
        new("tenant"),
        new("workspace"),
        new("user", PrincipalKind.User),
        "session",
        AuthAssurance.Password,
        "correlation",
        "idempotency",
        grants.ToHashSet(StringComparer.Ordinal));

    private sealed class FakeEffectHandler(EffectDisposition disposition) : IEffectHandler
    {
        public string EffectType => "fake";
        public int Calls { get; private set; }

        public Task<EffectExecutionResult> ExecuteAsync(
            OutboxRecord intent,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new EffectExecutionResult(disposition, "safe-result"));
        }
    }
}
