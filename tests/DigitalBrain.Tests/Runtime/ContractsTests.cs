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
