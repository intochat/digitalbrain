using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;
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
    public void Conversation_transitions_are_idempotent_take_over_expired_leases_and_archive_without_losing_sequence()
    {
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("tenant"), new("workspace"), new("principal", PrincipalKind.User), "conversation"));
        var first = ConversationTransitions.BeginOperation(
            state, state.Revision, "command-0", Hash("input-0"), "operation-0", "turn-0", Utc(0));
        var replay = ConversationTransitions.BeginOperation(
            first, first.Revision, "command-0", Hash("input-0"), "operation-0", "turn-0", Utc(0));
        Assert.Same(first, replay);
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
            Utc(204),
            null);
        var completed = ConversationTransitions.CompleteWithAssistant(
            takeover.State,
            takeover.State.Revision,
            "operation-0",
            ConversationOperationStatus.Succeeded,
            ConversationTerminalPolicy.NeverRetry,
            null,
            "assistant result",
            outbox,
            Utc(204));
        var completionReplay = ConversationTransitions.CompleteWithAssistant(
            completed,
            completed.Revision,
            "operation-0",
            ConversationOperationStatus.Succeeded,
            ConversationTerminalPolicy.NeverRetry,
            null,
            "assistant result",
            outbox,
            Utc(204));
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
            Utc(205));
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
    public async Task Conversation_compaction_persists_a_retrievable_authenticated_segment_chain()
    {
        var state = ConversationTransitions.Initialize(ConversationState.Empty(), 0, new(
            new("tenant"), new("workspace"), new("principal", PrincipalKind.User), "archive-conversation"));
        var scope = RuntimeStateKeys.Conversation(
            state.Identity!.TenantId,
            state.Identity.WorkspaceId,
            state.Identity.Principal,
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
            new("tenant"), new("workspace"), new("principal", PrincipalKind.User), "conversation"));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command",
            Hash("input"),
            "operation",
            "turn",
            Utc(0));
        var invalidFlow = new SuspendedInvocation(
            OAuthCallbackPaths.GoogleProvider,
            "gmail.search",
            Encoding.UTF8.GetBytes("{}"),
            "0123456789abcdef0123456789abcdef",
            Utc(10),
            "short");
        var invalidProvider = invalidFlow with
        {
            Provider = "github",
            AuthorizationFlowReference = OAuthFlowReference
        };
        var invalidTool = invalidFlow with
        {
            ToolId = "salesforce.query",
            AuthorizationFlowReference = OAuthFlowReference
        };

        Assert.Throws<ArgumentException>(() => ConversationTransitions.SuspendAuthorization(
            state,
            state.Revision,
            "operation",
            invalidFlow,
            Utc(1)));
        Assert.Throws<ArgumentException>(() => ConversationTransitions.SuspendAuthorization(
            state,
            state.Revision,
            "operation",
            invalidProvider,
            Utc(1)));
        Assert.Throws<ArgumentException>(() => ConversationTransitions.SuspendAuthorization(
            state,
            state.Revision,
            "operation",
            invalidTool,
            Utc(1)));
    }

    [Fact]
    public void Surface_feed_is_projection_idempotent_and_owns_action_and_ack_authority()
    {
        var now = Utc(0);
        var tokenHash = Hash("token");
        var state = SurfaceFeedTransitions.Initialize(SurfaceFeedState.Empty(), 0, new(
            new("tenant"), new("workspace"), new("principal", PrincipalKind.User)));
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
    public void Expired_surface_cannot_resurrect_after_projection_dedupe_eviction()
    {
        var createdAt = Utc(0);
        var state = SurfaceFeedTransitions.Initialize(SurfaceFeedState.Empty(), 0, new(
            new("tenant"), new("workspace"), new("principal", PrincipalKind.User)));
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

    [Fact]
    public void Synapse_converter_hides_type_and_content_and_rejects_authenticated_envelope_tamper()
    {
        var protector = Protector();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new EncryptedSynapseJsonConverter(
            protector,
            Hash("journal-scope"),
            [typeof(LlmResponse)]));
        Synapse value = new LlmResponse("private prompt", "private response", "private model");

        var json = JsonSerializer.Serialize(value, options);

        Assert.DoesNotContain("private prompt", json);
        Assert.DoesNotContain("private response", json);
        Assert.DoesNotContain(typeof(LlmResponse).FullName!, json);
        var roundTrip = Assert.IsType<LlmResponse>(JsonSerializer.Deserialize<Synapse>(json, options));
        Assert.Equal("private response", roundTrip.Response);

        var envelopeOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var envelope = JsonSerializer.Deserialize<EncryptedRuntimeStateEnvelope>(json, envelopeOptions)!;
        envelope.Signature[0] ^= 0x01;
        var tampered = JsonSerializer.Serialize(envelope, envelopeOptions);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Synapse>(tampered, options));
    }

    private static SessionState InitializeSession(SessionState state) => SessionTransitions.Initialize(
        state,
        state.Revision,
        "opaque-session",
        SessionAudiences.Mcp,
        new(new("tenant"), new("workspace"), new("principal", PrincipalKind.User)),
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
