using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Runtime;
using Orleans.Runtime;

namespace DigitalBrain.Tests.Runtime;

public sealed class EncryptedDomainStateTests
{
    private const string OAuthFlowReference = "abcdefghijklmnopqrstuvwxyzABCDEF";

    [Fact]
    public async Task Persistent_state_rolls_back_failed_writes_and_rejects_stale_revisions_without_mutation()
    {
        var storage = new FailingEncryptedPersistentState { FailWrites = true };
        var runtime = SessionPersistence(storage, Protector());
        var originalEnvelope = storage.State;

        await Assert.ThrowsAsync<IOException>(() => runtime.UpdateAsync(0, current => InitializeSession(current)));

        AssertEnvelopeEqual(originalEnvelope, storage.State);
        Assert.False(storage.RecordExists);
        Assert.Equal("etag-0", storage.Etag);
        Assert.Equal(0, (await runtime.ReadAsync()).Revision);

        storage.FailWrites = false;
        var committed = await runtime.UpdateAsync(0, current => InitializeSession(current));
        var committedEnvelope = storage.State;
        var committedEtag = storage.Etag;
        Assert.Equal(1, committed.Revision);

        var conflict = await Assert.ThrowsAsync<RuntimeStateConflictException>(() =>
            runtime.UpdateAsync(0, current => SessionTransitions.Revoke(current, 0, Utc(5))));

        Assert.Equal(0, conflict.ExpectedRevision);
        Assert.Equal(1, conflict.ActualRevision);
        Assert.Same(committedEnvelope, storage.State);
        Assert.Equal(committedEtag, storage.Etag);
        Assert.Equal(2, storage.WriteAttempts);
    }

    [Fact]
    public async Task Persistent_state_accepts_a_verified_commit_when_the_provider_response_is_lost()
    {
        var storage = new FailingEncryptedPersistentState { CommitThenThrow = true };
        var runtime = SessionPersistence(storage, Protector());

        var committed = await runtime.UpdateAsync(0, InitializeSession);

        Assert.Equal(1, committed.Revision);
        Assert.Equal(1, (await runtime.ReadAsync()).Revision);
        Assert.Equal(1, storage.WriteAttempts);
        var conflict = await Assert.ThrowsAsync<RuntimeStateConflictException>(() =>
            runtime.UpdateAsync(0, current => SessionTransitions.Revoke(current, 0, Utc(5))));
        Assert.Equal(1, conflict.ActualRevision);
        Assert.Equal(1, storage.WriteAttempts);
    }

    [Fact]
    public async Task Envelopes_fail_closed_on_tamper_wrong_keys_and_rewrap_with_the_active_kek()
    {
        var scope = RuntimeStateKeys.Session("opaque-session");
        var signingKey = Key(90);
        var oldProtector = Protector(1, new Dictionary<int, byte[]> { [1] = Key(11) }, signingKey);
        var state = InitializeSession(SessionState.Empty());
        var tampered = oldProtector.Protect(scope, RuntimeStateKinds.Session, RuntimeStateSchemas.Session, state.Revision, state);
        tampered.PayloadCiphertext[0] ^= 0x80;

        Assert.Throws<RuntimeStateIntegrityException>(() => oldProtector.Unprotect<SessionState>(
            scope, RuntimeStateKinds.Session, RuntimeStateSchemas.Session, tampered));

        var envelope = oldProtector.Protect(scope, RuntimeStateKinds.Session, RuntimeStateSchemas.Session, state.Revision, state);
        var wrongKek = Protector(1, new Dictionary<int, byte[]> { [1] = Key(12) }, signingKey);
        var wrongSignature = Protector(1, new Dictionary<int, byte[]> { [1] = Key(11) }, Key(91));
        Assert.Throws<RuntimeStateIntegrityException>(() => wrongKek.Unprotect<SessionState>(
            scope, RuntimeStateKinds.Session, RuntimeStateSchemas.Session, envelope));
        Assert.Throws<RuntimeStateIntegrityException>(() => wrongSignature.Unprotect<SessionState>(
            scope, RuntimeStateKinds.Session, RuntimeStateSchemas.Session, envelope));

        var storage = new FailingEncryptedPersistentState { State = envelope, RecordExists = true };
        var rotated = Protector(2, new Dictionary<int, byte[]> { [1] = Key(11), [2] = Key(22) }, signingKey);
        var reopened = await SessionPersistence(storage, rotated).ReadAsync();

        Assert.Equal(state.Revision, reopened.Revision);
        Assert.Equal(2, storage.State.KekVersion);
        Assert.Equal(1, storage.WriteAttempts);
        Assert.Equal(state.OpaqueSessionId, rotated.Unprotect<SessionState>(
            scope, RuntimeStateKinds.Session, RuntimeStateSchemas.Session, storage.State).OpaqueSessionId);
    }

    [Fact]
    public void Conversation_state_with_legacy_null_grant_snapshots_stays_valid_and_accepts_new_operations()
    {
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation"));
        state = ConversationTransitions.BeginOperation(
            state, state.Revision, "command-0", Hash("input-0"), "operation-0", "turn-0",
            "request-command-0", AcceptedOutbox("operation-0", Utc(0)), Utc(0), ["mail.read"]);
        var legacyState = state with
        {
            AcceptedCommands = state.AcceptedCommands.Select(command => command with { Grants = null }).ToArray(),
            Operations = state.Operations.Select(operation => operation with { Grants = null }).ToArray()
        };

        ConversationTransitions.Validate(legacyState);
        var next = ConversationTransitions.BeginOperation(
            legacyState, legacyState.Revision, "command-1", Hash("input-1"), "operation-1", "turn-1",
            "request-command-1", AcceptedOutbox("operation-1", Utc(1)), Utc(1), ["mail.read"]);

        Assert.Null(next.AcceptedCommands.Single(command => command.CommandId == "command-0").Grants);
        var acceptedGrants = next.AcceptedCommands.Single(command => command.CommandId == "command-1").Grants;
        Assert.NotNull(acceptedGrants);
        Assert.Equal(["mail.read"], acceptedGrants);
        var operationGrants = next.Operations.Single(operation => operation.OperationId == "operation-1").Grants;
        Assert.NotNull(operationGrants);
        Assert.Equal(["mail.read"], operationGrants);
    }

    [Fact]
    public void Conversation_transitions_are_idempotent_take_over_expired_leases_and_archive_without_losing_sequence()
    {
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation"));
        var first = ConversationTransitions.BeginOperation(
            state, state.Revision, "command-0", Hash("input-0"), "operation-0", "turn-0",
            "request-command-0", AcceptedOutbox("operation-0", Utc(0)), Utc(0));
        var replay = ConversationTransitions.BeginOperation(
            first, first.Revision, "command-0", Hash("input-0"), "operation-0", "turn-0",
            "request-command-0-retry", new ConversationOutboxEntry(
                "accepted-operation-0", "surface-feed", [1], Utc(0), null), Utc(0));
        Assert.Same(first, replay);
        var accepted = Assert.Single(first.AcceptedCommands);
        Assert.Equal("command-0", accepted.CommandId);
        Assert.Equal("operation-0", accepted.OperationId);
        Assert.Equal("request-command-0", accepted.RequestId);
        Assert.Contains(first.Outbox, entry => entry.OutboxId == "accepted-operation-0");
        state = first;
        for (var index = 1; index < 140; index++)
        {
            state = ConversationTransitions.BeginOperation(
                state,
                state.Revision,
                "command-" + index,
                Hash("input-" + index),
                "operation-" + index,
                "turn-" + index,
                "request-command-" + index,
                AcceptedOutbox("operation-" + index, Utc(index)),
                Utc(index));
        }

        Assert.InRange(
            state.Turns.Length,
            ConversationArchiveTransitions.InlineTurnsAfterCompaction,
            ConversationTransitions.MaximumInlineTurns);
        Assert.Equal(33, state.Archive!.ArchivedTurnCount);
        Assert.Equal(140, state.Turns[^1].Sequence);

        var firstClaim = ConversationTransitions.TryClaimOperation(
            state, state.Revision, "operation-0", "worker-a", Utc(201), TimeSpan.FromMinutes(1));
        var blocked = ConversationTransitions.TryClaimOperation(
            firstClaim.State, firstClaim.State.Revision, "operation-0", "worker-b", Utc(201).AddSeconds(30), TimeSpan.FromMinutes(1));
        var takeover = ConversationTransitions.TryClaimOperation(
            blocked.State, blocked.State.Revision, "operation-0", "worker-b", Utc(201).AddMinutes(2), TimeSpan.FromMinutes(1));

        Assert.True(firstClaim.Claimed);
        Assert.False(blocked.Claimed);
        Assert.True(takeover.Claimed);
        Assert.Equal(2, takeover.Operation!.Attempt);
        Assert.Equal("worker-b", takeover.Operation.LeaseOwner);

        var outbox = new ConversationOutboxEntry(
            "feed-operation-0",
            "surface-feed",
            Encoding.UTF8.GetBytes("assistant surface"),
            Utc(203).AddSeconds(30),
            null);
        var completionAt = Utc(203).AddSeconds(30);
        var completed = ConversationTransitions.CompleteWithAssistant(
            takeover.State,
            takeover.State.Revision,
            "operation-0",
            ConversationOperationStatus.Succeeded,
            ConversationTerminalPolicy.NeverRetry,
            null,
            "assistant result",
            outbox,
            completionAt,
            leaseFence: new ConversationLeaseFence("worker-b", takeover.Operation!.Attempt));
        var completionReplay = ConversationTransitions.CompleteWithAssistant(
            completed,
            completed.Revision,
            "operation-0",
            ConversationOperationStatus.Succeeded,
            ConversationTerminalPolicy.NeverRetry,
            null,
            "assistant result",
            outbox,
            completionAt);
        Assert.Same(completed, completionReplay);
        Assert.Contains(completed.Turns, turn =>
            turn.OperationId == "operation-0" && turn.Kind == ConversationTurnKind.Assistant);
        Assert.Contains(completed.Outbox, entry => entry.OutboxId == outbox.OutboxId);

        var authorizationClaim = ConversationTransitions.TryClaimOperation(
            completed, completed.Revision, "operation-1", "worker-a", Utc(205), TimeSpan.FromMinutes(1));
        var invocation = new SuspendedInvocation(
            "salesforce",
            "salesforce.query",
            Encoding.UTF8.GetBytes("exact input"),
            "0123456789abcdef0123456789abcdef",
            Utc(300),
            OAuthFlowReference);
        var authorizationOutbox = new ConversationOutboxEntry(
            "feed-authorization-1",
            "surface-feed",
            Encoding.UTF8.GetBytes("authorization surface"),
            Utc(205),
            null);
        var suspended = ConversationTransitions.SuspendAuthorizationWithAssistant(
            authorizationClaim.State,
            authorizationClaim.State.Revision,
            "operation-1",
            invocation,
            "Authorization is required.",
            authorizationOutbox,
            Utc(205),
            new ConversationLeaseFence("worker-a", authorizationClaim.Operation!.Attempt));
        var suspensionReplay = ConversationTransitions.SuspendAuthorizationWithAssistant(
            suspended,
            suspended.Revision,
            "operation-1",
            invocation,
            "Authorization is required.",
            authorizationOutbox,
            Utc(205));
        Assert.Same(suspended, suspensionReplay);
        Assert.Contains(suspended.Turns, turn =>
            turn.OperationId == "operation-1" && turn.Kind == ConversationTurnKind.Authorization);
        Assert.Contains(suspended.Outbox, entry => entry.OutboxId == authorizationOutbox.OutboxId);
    }

    [Fact]
    public void Conversation_outbox_stamps_monotonic_sequences_and_preserves_them_on_replay()
    {
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation-sequence"));
        var first = ConversationTransitions.BeginOperation(
            state, state.Revision, "command-sequence-1", Hash("input-sequence-1"), "operation-sequence-1", "first",
            "request-sequence-1", AcceptedOutbox("operation-sequence-1", Utc(1)), Utc(1));
        var second = ConversationTransitions.BeginOperation(
            first, first.Revision, "command-sequence-2", Hash("input-sequence-2"), "operation-sequence-2", "second",
            "request-sequence-2", AcceptedOutbox("operation-sequence-2", Utc(1)), Utc(1));
        var replay = ConversationTransitions.BeginOperation(
            second, second.Revision, "command-sequence-2", Hash("input-sequence-2"), "operation-sequence-2", "second",
            "request-sequence-2", AcceptedOutbox("operation-sequence-2", Utc(1)), Utc(1));

        Assert.Equal(2, second.NextOutboxSequence);
        Assert.Equal([1L, 2L], second.Outbox.OrderBy(entry => entry.Sequence).Select(entry => entry.Sequence));
        Assert.Same(second, replay);
        Assert.Equal(2, replay.NextOutboxSequence);

        var legacy = second with
        {
            Outbox = second.Outbox.Select(entry => entry with { Sequence = 0 }).ToArray(),
            NextOutboxSequence = 0
        };
        var migrated = ConversationTransitions.MigrateLegacyOutboxSequences(legacy);

        Assert.Equal([1L, 2L], migrated.Outbox.Select(entry => entry.Sequence));
        Assert.Equal(2, migrated.NextOutboxSequence);
    }

    [Fact]
    public void Legacy_inbox_migrates_to_a_durable_receipt_and_replays_the_original_operation()
    {
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation-legacy-receipt"));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command-legacy",
            Hash("input-legacy"),
            "operation-legacy",
            "keep this accepted request",
            "request-original",
            AcceptedOutbox("operation-legacy", Utc(0)),
            Utc(0));
        var legacy = state with { AcceptedCommands = [] };

        var migrated = ConversationTransitions.MigrateLegacyAcceptedCommands(legacy);
        var replay = ConversationTransitions.BeginOperation(
            migrated,
            migrated.Revision,
            "command-legacy",
            Hash("input-legacy"),
            "operation-legacy",
            "keep this accepted request",
            "request-retry",
            new ConversationOutboxEntry("accepted-operation-legacy", "surface-feed", [2], Utc(1), null),
            Utc(1));

        Assert.Same(migrated, replay);
        Assert.Equal("request-original", Assert.Single(replay.AcceptedCommands).RequestId);
    }

    [Fact]
    public void Accepted_command_receipt_survives_inbox_compaction_and_replays_the_original_operation()
    {
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation-idempotency"));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command-replay",
            Hash("input-replay"),
            "operation-replay",
            "replay this request",
            "request-original",
            AcceptedOutbox("operation-replay", Utc(0)),
            Utc(0));
        for (var index = 1; index <= ConversationTransitions.MaximumInboxEntries + 1; index++)
        {
            state = ConversationTransitions.BeginOperation(
                state,
                state.Revision,
                "command-retained-" + index,
                Hash("input-retained-" + index),
                "operation-retained-" + index,
                "request " + index,
                "request-retained-" + index,
                AcceptedOutbox("operation-retained-" + index, Utc(index)),
                Utc(index));
        }

        Assert.DoesNotContain(state.Inbox, entry => entry.CommandId == "command-replay");
        Assert.Contains(state.AcceptedCommands, command => command.CommandId == "command-replay");

        var replay = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command-replay",
            Hash("input-replay"),
            "operation-replay",
            "replay this request",
            "request-retry",
            new ConversationOutboxEntry("accepted-operation-replay", "surface-feed", [2], Utc(0), null),
            Utc(0));

        Assert.Same(state, replay);
        Assert.Equal("request-original", replay.AcceptedCommands.Single(command =>
            command.CommandId == "command-replay").RequestId);
    }

    [Fact]
    public void Pending_outbox_is_bounded_without_discarding_undelivered_events()
    {
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation-outbox-cap"));
        for (var index = 0; index < ConversationTransitions.MaximumPendingOutboxEntries; index++)
        {
            state = ConversationTransitions.BeginOperation(
                state,
                state.Revision,
                "command-backlog-" + index,
                Hash("input-backlog-" + index),
                "operation-backlog-" + index,
                "queued request " + index,
                "request-backlog-" + index,
                AcceptedOutbox("operation-backlog-" + index, Utc(index)),
                Utc(index));
        }

        Assert.Equal(ConversationTransitions.MaximumPendingOutboxEntries,
            state.Outbox.Count(entry => entry.DispatchedAt is null));
        Assert.Throws<InvalidOperationException>(() => ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command-backlog-overflow",
            Hash("input-backlog-overflow"),
            "operation-backlog-overflow",
            "queued overflow request",
            "request-backlog-overflow",
            AcceptedOutbox("operation-backlog-overflow", Utc(600)),
            Utc(600)));
    }

    [Fact]
    public void Authorization_resume_claim_is_idempotent_and_cannot_be_reclaimed_as_normal_work()
    {
        var now = Utc(0);
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation"));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command",
            Hash("input"),
            "operation",
            "read mail",
            "request-command",
            AcceptedOutbox("operation", now),
            now);
        var invocation = new SuspendedInvocation(
            "google",
            "gmail.read.messages",
            Encoding.UTF8.GetBytes("{}"),
            "0123456789abcdef0123456789abcdef",
            now.AddMinutes(10),
            OAuthFlowReference,
            new WorkflowReference("agent-framework", "agent-framework-operation", "session-operation"));
        var workClaim = ConversationTransitions.TryClaimOperation(
            state,
            state.Revision,
            "operation",
            "worker",
            now,
            TimeSpan.FromMinutes(1));
        var awaiting = ConversationTransitions.SuspendAuthorizationWithAssistant(
            workClaim.State,
            workClaim.State.Revision,
            "operation",
            invocation,
            "Connect Google to continue.",
            new ConversationOutboxEntry("authorization-operation-v2", "surface-feed", [], now, null),
            now,
            new ConversationLeaseFence("worker", workClaim.Operation!.Attempt));
        var runningOutbox = new ConversationOutboxEntry("running-operation-v3", "surface-feed", [], now.AddSeconds(1), null);

        var suspendedOperation = Assert.Single(awaiting.Operations);
        Assert.Empty(suspendedOperation.SuspendedInvocation!.InputUtf8);
        Assert.Null(suspendedOperation.SuspendedInvocation.Workflow);
        Assert.Equal(invocation.Workflow, suspendedOperation.Workflow);

        var first = ConversationTransitions.TryClaimAuthorization(
            awaiting,
            awaiting.Revision,
            "operation",
            invocation.AuthorizationAttemptId,
            "worker",
            now.AddSeconds(1),
            TimeSpan.FromMinutes(1),
            runningOutbox);
        var duplicate = ConversationTransitions.TryClaimAuthorization(
            first.State,
            first.State.Revision,
            "operation",
            invocation.AuthorizationAttemptId,
            "worker",
            now.AddSeconds(2),
            TimeSpan.FromMinutes(1),
            runningOutbox);
        var normalClaim = ConversationTransitions.TryClaimOperation(
            duplicate.State,
            duplicate.State.Revision,
            "operation",
            "worker",
            now.AddSeconds(2),
            TimeSpan.FromMinutes(1),
            new ConversationOutboxEntry("running-operation-v4", "surface-feed", [], now.AddSeconds(2), null));
        var recoveredAuthorization = ConversationTransitions.TryClaimAuthorization(
            duplicate.State,
            duplicate.State.Revision,
            "operation",
            invocation.AuthorizationAttemptId,
            "worker-restarted",
            now.AddMinutes(2),
            TimeSpan.FromMinutes(1),
            new ConversationOutboxEntry("running-operation-v5", "surface-feed", [], now.AddMinutes(2), null));

        Assert.True(first.Claimed);
        Assert.True(duplicate.Claimed);
        Assert.False(normalClaim.Claimed);
        Assert.Single(duplicate.State.Outbox, entry => entry.OutboxId == runningOutbox.OutboxId);
        Assert.DoesNotContain(duplicate.State.Outbox, entry => entry.OutboxId == "running-operation-v4");
        Assert.True(recoveredAuthorization.Acquired);
        Assert.Equal("worker-restarted", recoveredAuthorization.Operation!.LeaseOwner);
        Assert.Equal(first.Operation!.Attempt + 1, recoveredAuthorization.Operation!.Attempt);
    }

    [Fact]
    public void Worker_terminal_transition_requires_a_lease_fence()
    {
        var now = Utc(0);
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation"));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command",
            Hash("input"),
            "operation",
            "answer the request",
            "request-command",
            AcceptedOutbox("operation", now),
            now);
        var claim = ConversationTransitions.TryClaimOperation(
            state,
            state.Revision,
            "operation",
            "worker",
            now,
            TimeSpan.FromMinutes(1));

        Assert.True(claim.Acquired);
        Assert.Throws<InvalidOperationException>(() => ConversationTransitions.CompleteWithAssistant(
            claim.State,
            claim.State.Revision,
            "operation",
            ConversationOperationStatus.Succeeded,
            ConversationTerminalPolicy.NeverRetry,
            null,
            "The request completed.",
            new ConversationOutboxEntry("completed-operation", "surface-feed", [], now, null),
            now));
    }

    [Fact]
    public void Terminal_completion_rejects_a_feature_proposal_route_outside_the_proposals_namespace()
    {
        var now = Utc(0);
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation"));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command",
            Hash("input"),
            "operation",
            "answer the request",
            "request-command",
            AcceptedOutbox("operation", now),
            now);
        var claim = ConversationTransitions.TryClaimOperation(
            state,
            state.Revision,
            "operation",
            "worker",
            now,
            TimeSpan.FromMinutes(1));
        var leaseFence = new ConversationLeaseFence("worker", claim.Operation!.Attempt);

        Assert.Throws<ArgumentException>(() => ConversationTransitions.CompleteWithAssistant(
            claim.State,
            claim.State.Revision,
            "operation",
            ConversationOperationStatus.Succeeded,
            ConversationTerminalPolicy.NeverRetry,
            null,
            "The request completed.",
            new ConversationOutboxEntry("completed-operation-external", "surface-feed", [], now, null),
            now,
            leaseFence: leaseFence,
            proposal: new FeatureDraftReference("proposal-0123456789abcdef0123456789abcdef", "Open Studio", "https://example.com")));

        Assert.Throws<ArgumentException>(() => ConversationTransitions.CompleteWithAssistant(
            claim.State,
            claim.State.Revision,
            "operation",
            ConversationOperationStatus.Succeeded,
            ConversationTerminalPolicy.NeverRetry,
            null,
            "The request completed.",
            new ConversationOutboxEntry("completed-operation-other", "surface-feed", [], now, null),
            now,
            leaseFence: leaseFence,
            proposal: new FeatureDraftReference("proposal-0123456789abcdef0123456789abcdef", "Open Studio", "/other")));
    }

    [Fact]
    public void Terminal_completion_rejects_a_feature_proposal_route_that_deviates_from_the_anchored_shape()
    {
        var now = Utc(0);
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation"));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command",
            Hash("input"),
            "operation",
            "answer the request",
            "request-command",
            AcceptedOutbox("operation", now),
            now);
        var claim = ConversationTransitions.TryClaimOperation(
            state,
            state.Revision,
            "operation",
            "worker",
            now,
            TimeSpan.FromMinutes(1));
        var leaseFence = new ConversationLeaseFence("worker", claim.Operation!.Attempt);
        const string validId = "proposal-0123456789abcdef0123456789abcdef";
        const string validRoute = "/features/proposals/" + validId;
        string[] deviantRoutes =
        [
            validRoute + "/extra",
            validRoute + "?query=1",
            "/features/proposals/proposal-0123456789ABCDEF0123456789ABCDEF",
            "/features/proposals/proposal-0123456789abcdef0123456789abcde"
        ];

        foreach (var route in deviantRoutes)
        {
            var outboxId = "completed-operation-" + route.GetHashCode();
            Assert.Throws<ArgumentException>(() => ConversationTransitions.CompleteWithAssistant(
                claim.State,
                claim.State.Revision,
                "operation",
                ConversationOperationStatus.Succeeded,
                ConversationTerminalPolicy.NeverRetry,
                null,
                "The request completed.",
                new ConversationOutboxEntry(outboxId, "surface-feed", [], now, null),
                now,
                leaseFence: leaseFence,
                proposal: new FeatureDraftReference(validId, "Open Studio", route)));
        }
    }

    [Fact]
    public void Terminal_completion_accepts_the_exact_anchored_proposal_route_shape()
    {
        var now = Utc(0);
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation"));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command",
            Hash("input"),
            "operation",
            "answer the request",
            "request-command",
            AcceptedOutbox("operation", now),
            now);
        var claim = ConversationTransitions.TryClaimOperation(
            state,
            state.Revision,
            "operation",
            "worker",
            now,
            TimeSpan.FromMinutes(1));
        var leaseFence = new ConversationLeaseFence("worker", claim.Operation!.Attempt);
        const string proposalId = "proposal-0123456789abcdef0123456789abcdef";

        var completed = ConversationTransitions.CompleteWithAssistant(
            claim.State,
            claim.State.Revision,
            "operation",
            ConversationOperationStatus.Succeeded,
            ConversationTerminalPolicy.NeverRetry,
            null,
            "The request completed.",
            new ConversationOutboxEntry("completed-operation", "surface-feed", [], now, null),
            now,
            leaseFence: leaseFence,
            proposal: new FeatureDraftReference(proposalId, "Open Studio", "/features/proposals/" + proposalId));

        Assert.Contains(completed.Turns, turn =>
            turn.OperationId == "operation" && turn.Kind == ConversationTurnKind.Assistant);
    }

    [Fact]
    public void Terminal_completion_rejects_out_of_bounds_capability_receipts()
    {
        CapabilityResolutionReceipt[] invalidReceipts =
        [
            CapabilityReceipt(capabilityId: new string('a', 129)),
            CapabilityReceipt(capabilityName: new string('n', 81)),
            CapabilityReceipt(capabilityName: "Read\nrecords"),
            CapabilityReceipt(candidateIds: Enumerable.Range(0, 6).Select(index => "candidate-" + index).ToArray()),
            CapabilityReceipt(candidateIds: [new string('c', 129)]),
            CapabilityReceipt(confidence: -0.1),
            CapabilityReceipt(confidence: 1.1),
            CapabilityReceipt(confidence: double.NaN)
        ];

        Assert.All(invalidReceipts, receipt =>
            Assert.Throws<ArgumentException>(() => CompleteWithCapability(receipt)));
    }

    [Fact]
    public void Terminal_completion_accepts_capability_receipts_at_exact_bounds()
    {
        var lowerBound = CapabilityReceipt(
            capabilityId: new string('a', 128),
            capabilityName: new string('n', 80),
            candidateIds: Enumerable.Range(0, 5).Select(index => index + new string('c', 127)).ToArray(),
            confidence: 0);
        var upperBound = CapabilityReceipt(confidence: 1);

        var lowerBoundState = CompleteWithCapability(lowerBound);
        var upperBoundState = CompleteWithCapability(upperBound);

        var lowerBoundOperation = lowerBoundState.Operations.Single(operation => operation.OperationId == "operation");
        Assert.Equal(ConversationOperationStatus.Succeeded, lowerBoundOperation.Status);
        Assert.Equal(lowerBound.CapabilityId, lowerBoundOperation.Capability!.CapabilityId);
        Assert.Equal(lowerBound.CapabilityName, lowerBoundOperation.Capability.CapabilityName);
        Assert.Equal(lowerBound.CandidateIds, lowerBoundOperation.Capability.CandidateIds);
        Assert.Equal(0, lowerBoundOperation.Capability.Confidence);
        var upperBoundOperation = upperBoundState.Operations.Single(operation => operation.OperationId == "operation");
        Assert.Equal(1, upperBoundOperation.Capability!.Confidence);
    }

    private static CapabilityResolutionReceipt CapabilityReceipt(
        string? capabilityId = "capability.read.v1",
        string? capabilityName = "Read records",
        string[]? candidateIds = null,
        double confidence = 0.5) =>
        new(CapabilityResolutionKind.Match, capabilityId, capabilityName, candidateIds ?? [], confidence);

    private static ConversationState CompleteWithCapability(CapabilityResolutionReceipt receipt)
    {
        var now = Utc(0);
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation"));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command",
            Hash("input"),
            "operation",
            "answer the request",
            "request-command",
            AcceptedOutbox("operation", now),
            now);
        var claim = ConversationTransitions.TryClaimOperation(
            state,
            state.Revision,
            "operation",
            "worker",
            now,
            TimeSpan.FromMinutes(1));
        return ConversationTransitions.CompleteWithAssistant(
            claim.State,
            claim.State.Revision,
            "operation",
            ConversationOperationStatus.Succeeded,
            ConversationTerminalPolicy.NeverRetry,
            null,
            "The request completed.",
            new ConversationOutboxEntry("completed-operation", "surface-feed", [], now, null),
            now,
            leaseFence: new ConversationLeaseFence("worker", claim.Operation!.Attempt),
            capability: receipt);
    }

    [Fact]
    public void Same_owner_claim_does_not_reacquire_and_a_stale_fence_cannot_complete()
    {
        var now = Utc(0);
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation"));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command",
            Hash("input"),
            "operation",
            "answer the request",
            "request-command",
            AcceptedOutbox("operation", now),
            now);
        var firstClaim = ConversationTransitions.TryClaimOperation(
            state,
            state.Revision,
            "operation",
            "worker-a",
            now,
            TimeSpan.FromMinutes(1));
        var duplicateClaim = ConversationTransitions.TryClaimOperation(
            firstClaim.State,
            firstClaim.State.Revision,
            "operation",
            "worker-a",
            now.AddSeconds(1),
            TimeSpan.FromMinutes(1));
        var takeover = ConversationTransitions.TryClaimOperation(
            duplicateClaim.State,
            duplicateClaim.State.Revision,
            "operation",
            "worker-b",
            now.AddMinutes(2),
            TimeSpan.FromMinutes(1));

        Assert.True(firstClaim.Acquired);
        Assert.True(duplicateClaim.Claimed);
        Assert.False(duplicateClaim.Acquired);
        Assert.Equal(firstClaim.Operation!.Attempt, duplicateClaim.Operation!.Attempt);
        Assert.True(takeover.Acquired);
        Assert.Throws<RuntimeStateConflictException>(() => ConversationTransitions.CompleteWithAssistant(
            takeover.State,
            takeover.State.Revision,
            "operation",
            ConversationOperationStatus.Succeeded,
            ConversationTerminalPolicy.NeverRetry,
            null,
            "The request completed.",
            new ConversationOutboxEntry("completed-operation", "surface-feed", [], now.AddMinutes(2), null),
            now.AddMinutes(2),
            leaseFence: new ConversationLeaseFence("worker-a", firstClaim.Operation.Attempt)));
    }

    [Fact]
    public void Expired_authorization_handoff_is_claimed_before_recording_its_safe_outcome()
    {
        var now = Utc(0);
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation"));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command",
            Hash("input"),
            "operation",
            "read mail",
            "request-command",
            AcceptedOutbox("operation", now),
            now);
        var workClaim = ConversationTransitions.TryClaimOperation(
            state,
            state.Revision,
            "operation",
            "worker-a",
            now,
            TimeSpan.FromMinutes(1));
        var invocation = new SuspendedInvocation(
            "google",
            "gmail.read.messages",
            Encoding.UTF8.GetBytes("{}"),
            "0123456789abcdef0123456789abcdef",
            now.AddMinutes(1),
            OAuthFlowReference);
        var awaitingAuthorization = ConversationTransitions.SuspendAuthorizationWithAssistant(
            workClaim.State,
            workClaim.State.Revision,
            "operation",
            invocation,
            "Connect Google to continue.",
            new ConversationOutboxEntry("authorization-operation", "surface-feed", [], now, null),
            now,
            new ConversationLeaseFence("worker-a", workClaim.Operation!.Attempt));

        var authorizationClaim = ConversationTransitions.TryClaimAuthorization(
            awaitingAuthorization,
            awaitingAuthorization.Revision,
            "operation",
            invocation.AuthorizationAttemptId,
            "worker-b",
            now.AddMinutes(2),
            TimeSpan.FromMinutes(1));

        Assert.True(authorizationClaim.Acquired);
        Assert.Equal(ConversationOperationStatus.Running, authorizationClaim.Operation!.Status);
        Assert.Equal(2, authorizationClaim.Operation.Attempt);
    }

    [Fact]
    public void Mutation_approval_request_is_persisted_with_a_distinct_phase_outbox()
    {
        var now = Utc(0);
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation"));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command",
            Hash("input"),
            "operation",
            "change a record",
            "request-command",
            AcceptedOutbox("operation", now),
            now);
        var approval = new ApprovalRecord("approval-operation", "operation", "effect-operation", "requested", 1, now);
        var effect = new EffectRecord(
            "effect-operation",
            "operation",
            "salesforce.record.update",
            "workspace",
            "awaiting-approval",
            "effect-operation",
            1);
        var outbox = new ConversationOutboxEntry("approval-operation-v2", "surface-feed", [], now.AddSeconds(1), null);
        var workClaim = ConversationTransitions.TryClaimOperation(
            state,
            state.Revision,
            "operation",
            "worker",
            now,
            TimeSpan.FromMinutes(1));

        var requested = ConversationTransitions.RequestApprovalWithAssistant(
            workClaim.State,
            workClaim.State.Revision,
            "operation",
            approval,
            effect,
            "Approval is required before INO can perform this change.",
            outbox,
            now.AddSeconds(1),
            leaseFence: new ConversationLeaseFence("worker", workClaim.Operation!.Attempt));
        var replay = ConversationTransitions.RequestApprovalWithAssistant(
            requested,
            requested.Revision,
            "operation",
            approval,
            effect,
            "Approval is required before INO can perform this change.",
            outbox,
            now.AddSeconds(1));

        var operation = Assert.Single(requested.Operations);
        Assert.Equal(ConversationOperationStatus.AwaitingApproval, operation.Status);
        Assert.Equal(approval, operation.Approval);
        Assert.Equal(effect, operation.Effect);
        Assert.Contains(requested.Outbox, entry => entry.OutboxId == outbox.OutboxId);
        Assert.NotEqual("accepted-operation", outbox.OutboxId);
        Assert.Same(requested, replay);
    }

    [Fact]
    public void Approval_decision_is_actor_bound_and_replays_without_a_second_transition()
    {
        var now = Utc(0);
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation"));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command",
            Hash("input"),
            "operation",
            "change a record",
            "request-command",
            AcceptedOutbox("operation", now),
            now);
        var claim = ConversationTransitions.TryClaimOperation(
            state,
            state.Revision,
            "operation",
            "worker",
            now,
            TimeSpan.FromMinutes(1));
        var approval = new ApprovalRecord("approval-operation", "operation", "effect-operation", "requested", 1, now);
        var effect = new EffectRecord(
            "effect-operation",
            "operation",
            "salesforce.record.update",
            "workspace",
            "awaiting-approval",
            "provider-key-operation",
            1);
        var requested = ConversationTransitions.RequestApprovalWithAssistant(
            claim.State,
            claim.State.Revision,
            "operation",
            approval,
            effect,
            "Approval is required before INO can perform this change.",
            new ConversationOutboxEntry("approval-request-operation", "surface-feed", [], now.AddSeconds(1), null),
            now.AddSeconds(1),
            leaseFence: new ConversationLeaseFence("worker", claim.Operation!.Attempt));
        var decisionOutbox = new ConversationOutboxEntry(
            "approval-decision-operation",
            "surface-feed",
            [],
            now.AddSeconds(2),
            null);

        Assert.Throws<InvalidOperationException>(() => ConversationTransitions.DecideApprovalWithAssistant(
            requested,
            requested.Revision,
            "operation",
            approval.ApprovalId,
            approved: true,
            "decision-operation",
            "another-actor",
            "Approval recorded. INO will apply the approved action.",
            decisionOutbox,
            now.AddSeconds(2)));

        var actor = RequestScope.Id(requested.Identity!.OwnerId, requested.Identity.ActorId);
        var decided = ConversationTransitions.DecideApprovalWithAssistant(
            requested,
            requested.Revision,
            "operation",
            approval.ApprovalId,
            approved: true,
            "decision-operation",
            actor,
            "Approval recorded. INO will apply the approved action.",
            decisionOutbox,
            now.AddSeconds(2));
        var replay = ConversationTransitions.DecideApprovalWithAssistant(
            decided,
            decided.Revision,
            "operation",
            approval.ApprovalId,
            approved: true,
            "decision-operation",
            actor,
            "Approval recorded. INO will apply the approved action.",
            decisionOutbox,
            now.AddSeconds(2));

        Assert.Same(decided, replay);
        var operation = Assert.Single(decided.Operations);
        Assert.Equal("decision-operation", operation.Approval!.DecisionId);
        Assert.Equal(actor, operation.Approval.DecidedBy);
        Assert.Equal("approved", operation.Effect!.State);
        Assert.Single(decided.Turns, turn => turn.IdempotencyKey == "decision-operation");
        Assert.Single(decided.Outbox, entry => entry.OutboxId == decisionOutbox.OutboxId);
        Assert.Throws<InvalidOperationException>(() => ConversationTransitions.DecideApprovalWithAssistant(
            decided,
            decided.Revision,
            "operation",
            approval.ApprovalId,
            approved: true,
            "decision-operation",
            actor,
            "Approval recorded. INO will apply the approved action.",
            decisionOutbox with { PayloadUtf8 = [1] },
            now.AddSeconds(2)));
    }

    [Fact]
    public void Approved_effect_completion_rejects_changes_to_immutable_intent()
    {
        var now = Utc(0);
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation"));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command",
            Hash("input"),
            "operation",
            "change a record",
            "request-command",
            AcceptedOutbox("operation", now),
            now);
        var workflowClaim = ConversationTransitions.TryClaimOperation(
            state,
            state.Revision,
            "operation",
            "workflow-worker",
            now,
            TimeSpan.FromMinutes(1));
        var approval = new ApprovalRecord("approval-operation", "operation", "effect-operation", "requested", 1, now);
        var effect = new EffectRecord(
            "effect-operation",
            "operation",
            "salesforce.record.update",
            "workspace",
            "awaiting-approval",
            "provider-key-operation",
            1);
        var awaitingApproval = ConversationTransitions.RequestApprovalWithAssistant(
            workflowClaim.State,
            workflowClaim.State.Revision,
            "operation",
            approval,
            effect,
            "Approval is required before INO can perform this change.",
            new ConversationOutboxEntry("approval-request-operation", "surface-feed", [], now.AddSeconds(1), null),
            now.AddSeconds(1),
            leaseFence: new ConversationLeaseFence("workflow-worker", workflowClaim.Operation!.Attempt));
        var actor = RequestScope.Id(
            awaitingApproval.Identity!.OwnerId,
            awaitingApproval.Identity.ActorId);
        var approved = ConversationTransitions.DecideApprovalWithAssistant(
            awaitingApproval,
            awaitingApproval.Revision,
            "operation",
            approval.ApprovalId,
            approved: true,
            "decision-operation",
            actor,
            "Approval recorded. INO will apply the approved action.",
            new ConversationOutboxEntry("approval-decision-operation", "surface-feed", [], now.AddSeconds(2), null),
            now.AddSeconds(2));
        var effectClaim = ConversationTransitions.TryClaimOperation(
            approved,
            approved.Revision,
            "operation",
            "effect-worker",
            now.AddSeconds(3),
            TimeSpan.FromMinutes(1));
        var applying = effectClaim.Operation!.Effect!;
        var succeeded = applying with { State = "succeeded", Version = checked(applying.Version + 1) };
        var terminalOutbox = new ConversationOutboxEntry(
            "effect-complete-operation",
            "surface-feed",
            [],
            now.AddSeconds(4),
            null);

        void Complete(EffectRecord candidate)
        {
            _ = ConversationTransitions.CompleteEffectWithAssistant(
                effectClaim.State,
                effectClaim.State.Revision,
                "operation",
                candidate,
                ConversationOperationStatus.Succeeded,
                ConversationTerminalPolicy.NeverRetry,
                null,
                "The approved action completed.",
                terminalOutbox,
                now.AddSeconds(4),
                new ConversationLeaseFence("effect-worker", effectClaim.Operation.Attempt));
        }

        Assert.Throws<ArgumentException>(() => Complete(succeeded with { EffectId = "other-effect" }));
        foreach (var altered in new[]
                 {
                     succeeded with { Kind = "salesforce.record.delete" },
                     succeeded with { Scope = "other-workspace" },
                     succeeded with { ProviderIdempotencyKey = "other-provider-key" }
                 })
            Assert.Throws<InvalidOperationException>(() => Complete(altered));

        var unchanged = Assert.Single(effectClaim.State.Operations);
        Assert.Equal("applying", unchanged.Effect!.State);
        Assert.Equal(applying.EffectId, unchanged.Effect.EffectId);
        Assert.Equal(applying.Kind, unchanged.Effect.Kind);
        Assert.Equal(applying.Scope, unchanged.Effect.Scope);
        Assert.Equal(applying.ProviderIdempotencyKey, unchanged.Effect.ProviderIdempotencyKey);
    }

    [Fact]
    public async Task Conversation_compaction_persists_a_retrievable_authenticated_segment_chain()
    {
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "archive-conversation"));
        var scope = RuntimeStateKeys.Conversation(
            state.Identity!.OwnerId,
            state.Identity.ActorId,
            state.Identity.ConversationId);
        var segments = new Dictionary<string, ConversationArchiveSegment>(StringComparer.Ordinal);
        for (var index = 0; index < 220; index++)
        {
            var next = ConversationTransitions.BeginOperation(
                state,
                state.Revision,
                "archive-command-" + index,
                Hash("archive-input-" + index),
                "archive-operation-" + index,
                "archive-turn-" + index,
                "request-archive-command-" + index,
                AcceptedOutbox("archive-operation-" + index, Utc(index)),
                Utc(index));
            var segment = ConversationArchiveTransitions.PrepareSegment(scope, state, next);
            if (segment is not null) segments.Add(segment.SegmentId, segment);
            state = next;
        }

        Assert.NotNull(state.Archive);
        Assert.NotEmpty(segments);
        var archived = new List<ConversationTurn>();
        ConversationArchiveCursor? cursor = null;
        do
        {
            var page = await ConversationArchiveTransitions.ReadPageAsync(
                scope,
                state.Archive,
                cursor,
                11,
                id => Task.FromResult(segments.GetValueOrDefault(id)));
            archived.AddRange(page.Turns);
            cursor = page.NextCursor;
        } while (cursor is not null);
        var allTurns = archived.Concat(state.Turns).OrderBy(static turn => turn.Sequence).ToArray();
        Assert.Equal(220, allTurns.Length);
        Assert.Equal(Enumerable.Range(0, 220).Select(index => "archive-turn-" + index),
            allTurns.Select(static turn => turn.Text));

        var head = segments[state.Archive!.HeadSegmentId];
        segments[head.SegmentId] = head with
        {
            Turns = head.Turns.Select((turn, index) => index == 0 ? turn with { Text = "changed" } : turn).ToArray()
        };
        await Assert.ThrowsAsync<RuntimeStateIntegrityException>(() =>
            ConversationArchiveTransitions.ReadPageAsync(
                scope,
                state.Archive,
                null,
                11,
                id => Task.FromResult(segments.GetValueOrDefault(id))));
    }

    [Fact]
    public void Conversation_rejects_noncanonical_authorization_continuations()
    {
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("owner"), new("principal"), "conversation"));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command",
            Hash("input"),
            "operation",
            "turn",
            "request-command",
            AcceptedOutbox("operation", Utc(0)),
            Utc(0));
        var invalidFlow = new SuspendedInvocation(
            "google",
            "gmail.search",
            Encoding.UTF8.GetBytes("{}"),
            "0123456789abcdef0123456789abcdef",
            Utc(10),
            "short");
        var invalidProvider = invalidFlow with
        {
            Provider = "GitHub",
            AuthorizationFlowReference = OAuthFlowReference
        };
        var invalidTool = invalidFlow with
        {
            ToolId = string.Empty,
            AuthorizationFlowReference = OAuthFlowReference
        };
        var claim = ConversationTransitions.TryClaimOperation(
            state,
            state.Revision,
            "operation",
            "worker",
            Utc(1),
            TimeSpan.FromMinutes(1));

        ConversationState Suspend(SuspendedInvocation invocation) => ConversationTransitions.SuspendAuthorizationWithAssistant(
            claim.State,
            claim.State.Revision,
            "operation",
            invocation,
            "Connect the account to continue.",
            new ConversationOutboxEntry("invalid-authorization", "surface-feed", [], Utc(1), null),
            Utc(1),
            new ConversationLeaseFence("worker", claim.Operation!.Attempt));

        Assert.Throws<ArgumentException>(() => Suspend(invalidFlow));
        Assert.Throws<ArgumentException>(() => Suspend(invalidProvider));
        Assert.Throws<ArgumentException>(() => Suspend(invalidTool));
    }

    [Fact]
    public void Surface_feed_is_projection_idempotent_and_owns_action_and_ack_authority()
    {
        var now = Utc(0);
        var tokenHash = Hash("token");
        var state = SurfaceFeedTransitions.Initialize(SurfaceFeedState.Empty(), 0, new(
            new("owner"), new("principal")));
        var binding = new SurfaceActionBinding(
            "binding",
            "surface",
            1,
            "salesforce.query",
            "schema:salesforce.query",
            "salesforce.read",
            3,
            tokenHash,
            2,
            0,
            now.AddHours(1),
            null,
            null);
        var projection = new SurfaceFeedProjection(
            "projection",
            "surface",
            1,
            Hash("payload"),
            Encoding.UTF8.GetBytes("private payload"),
            now,
            null,
            [binding]);
        state = SurfaceFeedTransitions.ApplyProjection(state, state.Revision, projection, now);
        Assert.Same(state, SurfaceFeedTransitions.ApplyProjection(state, state.Revision, projection, now));

        var sessionScope = RuntimeStateKeys.Session("session");
        state = SurfaceFeedTransitions.Acknowledge(
            state, state.Revision, sessionScope, state.LastSequence, now.AddHours(1), now);
        var consumed = SurfaceFeedTransitions.ConsumeAction(
            state, state.Revision, binding.BindingId, tokenHash, "idempotency", "operation", now);
        var replay = SurfaceFeedTransitions.ConsumeAction(
            consumed.State, consumed.State.Revision, binding.BindingId, tokenHash, "idempotency", "other-operation", now);

        Assert.True(consumed.Consumed);
        Assert.Equal("salesforce.read", consumed.AuthorizedBinding.RequiredGrant);
        Assert.Equal(3, consumed.AuthorizedBinding.ActionSchemaVersion);
        Assert.False(replay.Consumed);
        Assert.Equal("operation", replay.OperationId);
        state = SurfaceFeedTransitions.RevokeSession(replay.State, replay.State.Revision, sessionScope, now);
        Assert.Empty(state.Acknowledgements);
    }

    [Fact]
    public void Surface_feed_retains_each_versioned_phase_event_while_current_surface_stays_latest()
    {
        var now = Utc(0);
        var state = SurfaceFeedTransitions.Initialize(SurfaceFeedState.Empty(), 0, new(
            new("owner"), new("principal")));
        foreach (var phase in new[] { "accepted", "running", "succeeded" })
        {
            var revision = state.LastSequence + 1;
            state = SurfaceFeedTransitions.ApplyProjection(
                state,
                state.Revision,
                new SurfaceFeedProjection(
                    "phase-" + phase,
                    "workspace-home",
                    checked((int)revision),
                    Hash(phase),
                    Encoding.UTF8.GetBytes(phase),
                    now,
                    null,
                    []),
                now);
        }

        Assert.Single(state.CurrentSurfaces);
        Assert.Equal([1L, 2L, 3L], state.EventHistory.Select(record => record.Sequence));
        Assert.Equal(["accepted", "running", "succeeded"], state.EventHistory
            .Select(record => Encoding.UTF8.GetString(record.PayloadUtf8)));
    }

    [Fact]
    public void Expired_surface_cannot_resurrect_after_projection_dedupe_eviction()
    {
        var createdAt = Utc(0);
        var state = SurfaceFeedTransitions.Initialize(SurfaceFeedState.Empty(), 0, new(
            new("owner"), new("principal")));
        var expired = new SurfaceFeedProjection(
            "expired-projection",
            "expired-surface",
            1,
            Hash("expired-payload"),
            Encoding.UTF8.GetBytes("expired payload"),
            createdAt,
            createdAt.AddMinutes(1),
            []);
        state = SurfaceFeedTransitions.ApplyProjection(state, state.Revision, expired, createdAt);
        for (var index = 0; index < SurfaceFeedTransitions.MaximumProjectionDedupe; index++)
        {
            var projection = new SurfaceFeedProjection(
                "new-projection-" + index,
                "new-surface-" + index,
                1,
                Hash("new-payload-" + index),
                Encoding.UTF8.GetBytes("payload-" + index),
                createdAt.AddMinutes(index + 2),
                null,
                []);
            state = SurfaceFeedTransitions.ApplyProjection(
                state,
                state.Revision,
                projection,
                projection.CreatedAt);
        }
        Assert.DoesNotContain("expired-projection", state.AppliedProjectionIds);
        Assert.DoesNotContain(state.CurrentSurfaces, surface => surface.SurfaceId == expired.SurfaceId);

        state = SurfaceFeedTransitions.ApplyProjection(
            state,
            state.Revision,
            expired,
            createdAt.AddDays(1));

        Assert.DoesNotContain(state.CurrentSurfaces, surface => surface.SurfaceId == expired.SurfaceId);
        Assert.DoesNotContain(state.ActionBindings, binding => binding.SurfaceId == expired.SurfaceId);
    }

    [Fact]
    public void Session_rotation_preserves_authorization_snapshot_detects_replay_and_invalidates_old_versions()
    {
        var initial = InitializeSession(SessionState.Empty());
        Assert.Equal(AuthAssurance.Oidc, initial.Assurance);
        Assert.Equal(["salesforce.read", "ui.read"], initial.Grants);
        Assert.True(SessionTransitions.IsAccessValid(initial, 1, Utc(1)));

        var rotated = SessionTransitions.RotateRefresh(
            initial, initial.Revision, Hash("refresh-1"), Hash("refresh-2"), Utc(20), Utc(2));
        var replay = SessionTransitions.RotateRefresh(
            rotated.State, rotated.State.Revision, Hash("refresh-1"), Hash("refresh-3"), Utc(21), Utc(3));

        Assert.Equal(SessionRotationStatus.Rotated, rotated.Status);
        Assert.Equal(2, rotated.State.SessionVersion);
        Assert.Equal(SessionRotationStatus.Replay, replay.Status);
        Assert.False(SessionTransitions.IsAccessValid(rotated.State, 1, Utc(3)));
        Assert.True(SessionTransitions.IsAccessValid(rotated.State, 2, Utc(3)));

        var revoked = SessionTransitions.Revoke(rotated.State, rotated.State.Revision, Utc(4));
        Assert.Equal(3, revoked.SessionVersion);
        Assert.False(SessionTransitions.IsAccessValid(revoked, 2, Utc(4)));
        Assert.False(SessionTransitions.IsAccessValid(revoked, 3, Utc(4)));
    }

    private static SessionState InitializeSession(SessionState state) => SessionTransitions.Initialize(
        state,
        state.Revision,
        "opaque-session",
        SessionAudiences.Mcp,
        new(new("owner"), new("principal")),
        AuthAssurance.Oidc,
        ["ui.read", "salesforce.read", "ui.read"],
        Hash("refresh-1"),
        Utc(10));

    private static void AssertEnvelopeEqual(
        EncryptedRuntimeStateEnvelope expected,
        EncryptedRuntimeStateEnvelope actual)
    {
        Assert.Equal(expected.EnvelopeVersion, actual.EnvelopeVersion);
        Assert.Equal(expected.KekVersion, actual.KekVersion);
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.WrappedDekNonce, actual.WrappedDekNonce);
        Assert.Equal(expected.WrappedDekCiphertext, actual.WrappedDekCiphertext);
        Assert.Equal(expected.WrappedDekTag, actual.WrappedDekTag);
        Assert.Equal(expected.PayloadNonce, actual.PayloadNonce);
        Assert.Equal(expected.PayloadCiphertext, actual.PayloadCiphertext);
        Assert.Equal(expected.PayloadTag, actual.PayloadTag);
        Assert.Equal(expected.Signature, actual.Signature);
    }

    private static EncryptedPersistentState<SessionState> SessionPersistence(
        IPersistentState<EncryptedRuntimeStateEnvelope> storage,
        EncryptedRuntimeStateProtector protector) => new(
        storage,
        protector,
        RuntimeStateKeys.Session("opaque-session"),
        RuntimeStateKinds.Session,
        RuntimeStateSchemas.Session,
        SessionState.Empty,
        static state => state.Revision,
        SessionTransitions.Validate);

    private static EncryptedRuntimeStateProtector Protector() =>
        Protector(1, new Dictionary<int, byte[]> { [1] = Key(11) }, Key(90));

    private static EncryptedRuntimeStateProtector Protector(
        int activeVersion,
        IReadOnlyDictionary<int, byte[]> keys,
        byte[] signingKey) => new(new RuntimeStateKeyRing(activeVersion, keys, signingKey));

    private static byte[] Key(byte value) => Enumerable.Repeat(value, 32).ToArray();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static DateTimeOffset Utc(int minutes) =>
        DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddMinutes(minutes);

    private static ConversationOutboxEntry AcceptedOutbox(string operationId, DateTimeOffset createdAt) => new(
            "accepted-" + operationId,
            "surface-feed",
            [],
            createdAt,
            null);

    private sealed class FailingEncryptedPersistentState : IPersistentState<EncryptedRuntimeStateEnvelope>
    {
        private EncryptedRuntimeStateEnvelope _committedState = new();
        private string _committedEtag = "etag-0";
        private bool _committedRecordExists;

        public EncryptedRuntimeStateEnvelope State { get; set; } = new();
        public string Etag { get; set; } = "etag-0";
        public bool RecordExists { get; set; }
        public bool FailWrites { get; set; }
        public bool CommitThenThrow { get; set; }
        public int WriteAttempts { get; private set; }

        public Task ClearStateAsync()
        {
            State = new();
            Etag = "etag-clear";
            RecordExists = false;
            _committedState = State;
            _committedEtag = Etag;
            _committedRecordExists = false;
            return Task.CompletedTask;
        }

        public Task ReadStateAsync()
        {
            State = _committedState;
            Etag = _committedEtag;
            RecordExists = _committedRecordExists;
            return Task.CompletedTask;
        }

        public Task WriteStateAsync()
        {
            WriteAttempts++;
            if (FailWrites)
            {
                Etag = "etag-uncommitted";
                RecordExists = true;
                throw new IOException("Injected write failure.");
            }
            Etag = "etag-" + WriteAttempts;
            RecordExists = true;
            _committedState = State;
            _committedEtag = Etag;
            _committedRecordExists = true;
            if (CommitThenThrow)
            {
                CommitThenThrow = false;
                throw new IOException("Injected lost write response.");
            }
            return Task.CompletedTask;
        }
    }
}
