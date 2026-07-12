using System.Text.Json;
using DigitalBrain.Core.Runtime;
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
    public void Signed_action_capability_binds_the_session_scope_binding_and_expiry()
    {
        var now = new DateTimeOffset(2026, 7, 12, 10, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        var tokens = new SessionTokenService(Enumerable.Repeat((byte)7, 32).ToArray(), clock);
        var context = Context("ui.action");
        const string bindingTokenHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var expiresAt = now.AddMinutes(5);

        var token = tokens.IssueActionCapability(
            context,
            "ino.approval.decision",
            "workspace-home",
            7,
            bindingTokenHash,
            expiresAt);

        Assert.True(tokens.TryValidateActionCapability(
            token,
            context,
            "ino.approval.decision",
            "workspace-home",
            7,
            bindingTokenHash));
        Assert.False(tokens.TryValidateActionCapability(
            token,
            context with { SessionId = "other-session" },
            "ino.approval.decision",
            "workspace-home",
            7,
            bindingTokenHash));
        Assert.False(tokens.TryValidateActionCapability(
            token,
            context with { WorkspaceId = new WorkspaceId("other-workspace") },
            "ino.approval.decision",
            "workspace-home",
            7,
            bindingTokenHash));
        Assert.False(tokens.TryValidateActionCapability(
            token[..^1] + (token[^1] == 'A' ? "B" : "A"),
            context,
            "ino.approval.decision",
            "workspace-home",
            7,
            bindingTokenHash));

        clock.UtcNow = expiresAt.AddSeconds(1);
        Assert.False(tokens.TryValidateActionCapability(
            token,
            context,
            "ino.approval.decision",
            "workspace-home",
            7,
            bindingTokenHash));
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

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
