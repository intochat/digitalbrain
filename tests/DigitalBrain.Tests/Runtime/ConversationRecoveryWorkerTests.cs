extern alias McpProject;

using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using ConversationRecoveryWorker = McpProject::DigitalBrain.Mcp.ConversationRecoveryWorker;
using RuntimeSessionAuthority = McpProject::DigitalBrain.Mcp.RuntimeSessionAuthority;

namespace DigitalBrain.Tests.Runtime;

public sealed class ConversationRecoveryWorkerTests
{
    [Fact]
    public void Recovery_plan_keeps_outbox_work_and_waits_until_a_retry_is_due()
    {
        var now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        var state = ConversationTransitions.Initialize(
            ConversationState.Empty(),
            0,
            new ConversationIdentity(
                new TenantId("tenant"),
                new WorkspaceId("workspace"),
                new PrincipalRef("principal", PrincipalKind.User),
                "conversation"));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command",
            new string('a', 64),
            "operation",
            "recover this prompt",
            now);
        state = ConversationTransitions.ScheduleRetry(
            state,
            state.Revision,
            "operation",
            now.AddMinutes(1),
            "retry later",
            now);
        state = ConversationTransitions.EnqueueOutbox(
            state,
            state.Revision,
            new ConversationOutboxEntry("feed-operation", "surface-feed", [1], now, null));

        var early = ConversationRecoveryWorker.Plan(state, now.AddSeconds(59));
        Assert.True(early.HasPendingOutbox);
        Assert.Null(early.Command);

        var due = ConversationRecoveryWorker.Plan(state, now.AddMinutes(1));
        Assert.True(due.HasPendingOutbox);
        Assert.NotNull(due.Command);
        Assert.Equal("command", due.Command.CommandId);
        Assert.Equal("recover this prompt", due.Command.Prompt);
    }

    [Fact]
    public async Task Replay_revocation_retries_a_concurrent_refresh_revision()
    {
        var now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        var initial = SessionTransitions.Initialize(
            SessionState.Empty(),
            0,
            "0123456789abcdef0123456789abcdef",
            "digitalbrain-ui",
            new SessionIdentity(
                new TenantId("tenant"),
                new WorkspaceId("workspace"),
                new PrincipalRef("principal", PrincipalKind.User)),
            AuthAssurance.Oidc,
            ["ui.action"],
            new string('a', 64),
            now.AddDays(30));
        var advanced = SessionTransitions.RotateRefresh(
            initial,
            initial.Revision,
            new string('a', 64),
            new string('b', 64),
            now.AddDays(30),
            now).State;
        var neuron = new ConcurrentRefreshSessionNeuron(initial, advanced);

        await RuntimeSessionAuthority.RevokeAfterReplayAsync(
            neuron,
            initial,
            now,
            CancellationToken.None);

        Assert.Equal(2, neuron.RevokeCalls);
        Assert.Equal(now, neuron.Current.RevokedAt);
    }

    private sealed class ConcurrentRefreshSessionNeuron(
        SessionState initial,
        SessionState advanced) : ISessionNeuron
    {
        public SessionState Current { get; private set; } = initial;
        public int RevokeCalls { get; private set; }

        public Task<SessionState> ReadAsync() => Task.FromResult(Current);

        public Task<SessionState> RevokeAsync(long expectedRevision, DateTimeOffset revokedAt)
        {
            RevokeCalls++;
            if (RevokeCalls == 1)
            {
                Current = advanced;
                throw new RuntimeStateConflictException(expectedRevision, Current.Revision);
            }
            Current = SessionTransitions.Revoke(Current, expectedRevision, revokedAt);
            return Task.FromResult(Current);
        }

        public Task<SessionState> InitializeAsync(
            long expectedRevision,
            string opaqueSessionId,
            string audience,
            SessionIdentity identity,
            AuthAssurance assurance,
            string[] grants,
            string refreshTokenHash,
            DateTimeOffset refreshExpiresAt) => throw new NotSupportedException();

        public Task<SessionRotation> RotateRefreshAsync(
            long expectedRevision,
            string presentedRefreshHash,
            string replacementRefreshHash,
            DateTimeOffset replacementExpiresAt,
            DateTimeOffset at) => throw new NotSupportedException();

        public Task<bool> IsAccessValidAsync(long sessionVersion, DateTimeOffset at) =>
            throw new NotSupportedException();
    }
}
