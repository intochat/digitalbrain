extern alias McpProject;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Runtime;
using Orleans;
using Orleans.Runtime;
using ConversationStateClient = McpProject::DigitalBrain.Mcp.ConversationStateClient;
using RuntimeSurfaceFeed = McpProject::DigitalBrain.Mcp.RuntimeSurfaceFeed;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;

namespace DigitalBrain.Tests.Runtime;

public sealed class RuntimeSurfaceFeedTests
{
    [Fact]
    public async Task Feature_approval_surface_binds_exact_digest_revision_and_single_use_decision()
    {
        var now = new DateTimeOffset(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        var context = Context() with
        {
            Grants = new HashSet<string>(["ui.action", "feature.manage"], StringComparer.Ordinal)
        };
        var conversationNeuron = new FakeConversationNeuron(ConversationState.Empty());
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var feed = new RuntimeSurfaceFeed(
            new FakeClusterClient(conversationNeuron, surfaceFeedNeuron),
            clock,
            ActionCapabilities(clock));
        var approvalId = new string('a', 64);
        var digest = new ReleaseDigest(new string('b', 64));
        var approval = new FeatureApprovalSnapshot(
            approvalId,
            new FeatureInstallationId("email-summarizer"),
            new FeatureReleaseMetadata(
                digest,
                "sha256:" + new string('c', 64),
                FeatureSourceKind.Repository,
                ["gmail.message.read.v1"],
                []),
            ["gmail.message.read.v1"],
            [],
            FeatureApprovalStatus.Pending,
            null,
            null,
            7,
            [new FeatureGrantSpec(
                "gmail.message.read.v1",
                1,
                new ProviderConnectionId("google-primary"),
                "{\"maximumMessages\":20}",
                "google")]);

        await feed.PublishFeatureApprovalAsync(context, approval, CancellationToken.None);
        var prepared = await feed.PrepareSessionAsync(context, CancellationToken.None);
        var surface = prepared.State.CurrentSurfaces.Single(candidate =>
            string.Equals(candidate.SurfaceId, "surface.feature-approval", StringComparison.Ordinal));
        var binding = prepared.State.ActionBindings.Single(candidate =>
            string.Equals(candidate.ActionType, "feature.release.decision.v1", StringComparison.Ordinal));
        var token = prepared.ActionTokens[binding.BindingId].Token;
        using var persisted = JsonDocument.Parse(
            JsonSerializer.Deserialize<SurfaceFeedPresentation>(surface.PayloadUtf8)!.Payload.GetRawText());
        var data = persisted.RootElement.GetProperty("data");
        Assert.Equal(digest.Value, data.GetProperty("releaseDigest").GetString());
        Assert.Equal(7, data.GetProperty("revision").GetInt64());
        Assert.False(data.TryGetProperty("source", out _));

        var exact = JsonSerializer.SerializeToElement(new
        {
            approvalId,
            releaseDigest = digest.Value,
            expectedRevision = 7,
            decision = "approve",
            clientDecisionId = "feature-decision-0000000000000001"
        });
        var authorized = await feed.AuthorizeActionAsync(
            context,
            binding.BindingId,
            token,
            surface.SurfaceId,
            surface.SurfaceRevision,
            exact,
            CancellationToken.None);
        Assert.Equal("feature.release.decision.v1", authorized.Submission.ActionType);

        var replay = await Assert.ThrowsAsync<ActionRejectedException>(() => feed.AuthorizeActionAsync(
            context,
            binding.BindingId,
            token,
            surface.SurfaceId,
            surface.SurfaceRevision,
            exact,
            CancellationToken.None));
        Assert.Equal(ActionRejection.Replay, replay.Reason);
    }

    [Fact]
    public async Task BeginAsync_bounds_the_accepted_projection_to_the_latest_sixteen_turns()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);
        var existingTurns = Enumerable.Range(1, 16)
            .Select(index => new ConversationTurn(
                index,
                "assistant",
                $"historical turn {index}",
                now.AddMinutes(index),
                $"operation-{index}",
                ConversationTurnKind.Assistant,
                $"historical-command-{index}"))
            .ToArray();
        var conversationNeuron = new FakeConversationNeuron(new ConversationState(
            RuntimeStateSchemas.Conversation,
            16,
            ConversationLifecycle.Active,
            new ConversationIdentity(context.OwnerId, context.ActorId, conversationId),
            existingTurns,
            [],
            [],
            [],
            null,
            null,
            []));
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var client = new ConversationStateClient(
            new FakeClusterClient(conversationNeuron, surfaceFeedNeuron),
            new MutableTimeProvider(now));

        await client.BeginAsync(context, "new-command", "List the latest records.");

        var accepted = Assert.Single(conversationNeuron.Current.Outbox);
        Assert.True(OperationOutboxRecord.TryRead(accepted.PayloadUtf8, out var projection));
        Assert.NotNull(projection);
        Assert.Equal(16, projection.View!.Turns.Length);
        Assert.Equal("historical-command-2", projection.View.Turns[0].CommandId);
        Assert.Equal("new-command", projection.View.Turns[^1].CommandId);
    }

    [Fact]
    public async Task ReadPage_returns_each_ordered_phase_even_when_the_home_surface_is_replaced()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);

        var accepted = new InoConversationSnapshot(
            conversationId,
            1,
            [new InoConversationTurn("command-1", "user", "summarize the status", InoConversationStates.Queued)],
            [new InoConversationOperation(
                "operation-1",
                "command-1",
                "summarize the status",
                InoConversationStates.Queued,
                null,
                false,
                now)]);

        var conversationNeuron = new FakeConversationNeuron(ConversationState.Empty());
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var cluster = new FakeClusterClient(conversationNeuron, surfaceFeedNeuron);
        var feed = new RuntimeSurfaceFeed(cluster, clock, ActionCapabilities(clock));

        await SeedConversationPhaseAsync(surfaceFeedNeuron, context, accepted, "phase-accepted", now);
        await SeedConversationPhaseAsync(surfaceFeedNeuron, context, accepted with
        {
            Revision = 2,
            Operations = [accepted.CurrentOperation! with { State = InoConversationStates.Running, Version = 2 }]
        }, "phase-running", now);
        await SeedConversationPhaseAsync(surfaceFeedNeuron, context, accepted with
        {
            Revision = 3,
            Turns = [.. accepted.Turns, new InoConversationTurn("operation-1", "assistant", "The status is ready.", InoConversationStates.Succeeded)],
            Operations = [accepted.CurrentOperation! with { State = InoConversationStates.Succeeded, Version = 3 }]
        }, "phase-succeeded", now);

        var state = await feed.ReadAsync(context, CancellationToken.None);
        var page = feed.ReadPage(context, state, 0, 100);

        Assert.Single(state.CurrentSurfaces);
        Assert.Equal([1L, 2L, 3L], page.Items.Select(item => item.Sequence));
        Assert.Equal(3, page.Items.Count);

        var prepared = await feed.PrepareSessionAsync(context, CancellationToken.None);
        Assert.Equal(state.Revision, prepared.State.Revision);
        Assert.Contains(ConversationSurfacePayload.SendBindingId, prepared.ActionTokens.Keys);

        var ahead = feed.ReadPage(context, state, 99, 100);
        Assert.True(ahead.ResetRequired);
        Assert.Single(ahead.Items);
    }

    [Fact]
    public async Task PrepareSessionAsync_uses_the_typed_home_surface_transition_once_then_issues_read_only_action_capabilities()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);

        var conversationNeuron = new FakeConversationNeuron(
            BuildConversationState(context, conversationId, revision: 1, assistantText: "Hello.", now));
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var cluster = new FakeClusterClient(conversationNeuron, surfaceFeedNeuron);
        var feed = new RuntimeSurfaceFeed(cluster, TimeProvider.System, ActionCapabilities());

        var first = await feed.PrepareSessionAsync(context, CancellationToken.None);
        var second = await feed.PrepareSessionAsync(context, CancellationToken.None);

        var firstHome = first.State.CurrentSurfaces.Single(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        var secondHome = second.State.CurrentSurfaces.Single(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        Assert.Equal(
            Encoding.UTF8.GetString(firstHome.PayloadUtf8),
            Encoding.UTF8.GetString(secondHome.PayloadUtf8));
        Assert.Equal(first.State.Revision, second.State.Revision);
        Assert.Equal(first.State.LastSequence, second.State.LastSequence);
        Assert.Equal(1, surfaceFeedNeuron.HomeSurfaceTransitions);
        Assert.Equal(0, surfaceFeedNeuron.GenericProjectionCalls);
    }

    [Fact]
    public void EnsureHomeSurface_rejects_a_canonical_conversation_id_from_another_scope()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var context = Context();
        var state = SurfaceFeedTransitions.Initialize(
            SurfaceFeedState.Empty(),
            0,
            new SurfaceFeedIdentity(context.OwnerId, context.ActorId));
        var expectedConversationId = InoConversationIdentity.From(context);
        var wrongConversationId = expectedConversationId[..^1] +
                                  (expectedConversationId[^1] == 'a' ? "b" : "a");

        var exception = Assert.Throws<ArgumentException>(() => SurfaceFeedTransitions.EnsureHomeSurface(
            state,
            state.Revision,
            new HomeSurfaceBootstrap("bootstrap-other-scope", wrongConversationId, "request-bootstrap", now)));

        Assert.Equal("bootstrap", exception.ParamName);
    }

    [Fact]
    public async Task PrepareSessionAsync_renews_an_expired_action_as_a_new_authoritative_surface_event()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);
        var conversationNeuron = new FakeConversationNeuron(
            BuildConversationState(context, conversationId, revision: 1, assistantText: "Hello.", now));
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var feed = new RuntimeSurfaceFeed(
            new FakeClusterClient(conversationNeuron, surfaceFeedNeuron),
            clock,
            ActionCapabilities(clock));

        var initial = await feed.PrepareSessionAsync(context, CancellationToken.None);
        var initialSurface = initial.State.CurrentSurfaces.Single(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        var initialBinding = initial.State.ActionBindings.Single(binding =>
            string.Equals(binding.BindingId, ConversationSurfacePayload.SendBindingId, StringComparison.Ordinal));
        clock.UtcNow = initialBinding.ExpiresAt.AddSeconds(1);

        var renewed = await feed.PrepareSessionAsync(context, CancellationToken.None);
        var renewedSurface = renewed.State.CurrentSurfaces.Single(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        var renewedBinding = renewed.State.ActionBindings.Single(binding =>
            string.Equals(binding.BindingId, ConversationSurfacePayload.SendBindingId, StringComparison.Ordinal));

        Assert.True(renewedBinding.ExpiresAt > clock.UtcNow);
        Assert.True(renewed.State.Revision > initial.State.Revision);
        Assert.Equal(initial.State.LastSequence + 1, renewed.State.LastSequence);
        Assert.Equal(initialSurface.SurfaceRevision + 1, renewedSurface.SurfaceRevision);
        Assert.NotEqual(initialSurface.ContentHash, renewedSurface.ContentHash);
        Assert.Equal(initial.State.EventHistory, renewed.State.EventHistory[..^1]);
        var delivered = Assert.Single(feed.ReadPage(context, renewed.State, initial.State.LastSequence, 100).Items);
        Assert.Equal(renewedSurface.SurfaceRevision, delivered.Revision);
        Assert.Equal(ConversationSurfacePayload.SendBindingId, Assert.Single(delivered.Actions).BindingId);
        Assert.True(renewed.ActionTokens.ContainsKey(ConversationSurfacePayload.SendBindingId));

        var authorized = await feed.AuthorizeActionAsync(
            context,
            ConversationSurfacePayload.SendBindingId,
            renewed.ActionTokens[ConversationSurfacePayload.SendBindingId].Token,
            renewedSurface.SurfaceId,
            renewedSurface.SurfaceRevision,
            JsonSerializer.SerializeToElement(new { prompt = "Hello" }),
            CancellationToken.None);

        Assert.Equal(ConversationSurfacePayload.SendActionType, authorized.Submission.ActionType);
    }

    [Fact]
    public async Task PrepareSessionAsync_does_not_reissue_a_consumed_action_before_the_next_projection()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);
        var conversationNeuron = new FakeConversationNeuron(
            BuildConversationState(context, conversationId, revision: 1, assistantText: "Hello.", now));
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var feed = new RuntimeSurfaceFeed(
            new FakeClusterClient(conversationNeuron, surfaceFeedNeuron),
            clock,
            ActionCapabilities(clock));
        var initial = await feed.PrepareSessionAsync(context, CancellationToken.None);
        var binding = Assert.Single(initial.State.ActionBindings);
        surfaceFeedNeuron.Current = initial.State with
        {
            ActionBindings =
            [
                binding with
                {
                    Uses = binding.MaxUses,
                    LastIdempotencyKey = "accepted-request",
                    LastOperationId = "accepted-operation"
                }
            ]
        };
        clock.UtcNow = binding.ExpiresAt.AddSeconds(1);

        var prepared = await feed.PrepareSessionAsync(context, CancellationToken.None);

        Assert.Equal(initial.State.LastSequence, prepared.State.LastSequence);
        Assert.Equal(initial.State.EventHistory, prepared.State.EventHistory);
        Assert.Empty(prepared.ActionTokens);
    }

    [Fact]
    public async Task PrepareSessionAsync_replaces_legacy_conversation_lifecycle_bindings_with_an_authoritative_event()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);
        var conversationNeuron = new FakeConversationNeuron(
            BuildConversationState(context, conversationId, revision: 1, assistantText: "Hello.", now));
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var clock = new MutableTimeProvider(now);
        var feed = new RuntimeSurfaceFeed(
            new FakeClusterClient(conversationNeuron, surfaceFeedNeuron),
            clock,
            ActionCapabilities(clock));
        var initial = await feed.PrepareSessionAsync(context, CancellationToken.None);
        var surface = Assert.Single(initial.State.CurrentSurfaces);
        var legacyExpiry = Assert.Single(initial.State.ActionBindings).ExpiresAt;
        surfaceFeedNeuron.Current = initial.State with
        {
            ActionBindings =
            [
                new SurfaceActionBinding(
                    "ino.new", surface.SurfaceId, surface.SurfaceRevision, "ino.conversation.new",
                    "digitalbrain.ino.conversation-new.v1", "ui.action", 1, Hash("legacy-new"),
                    1, 0, legacyExpiry, null, null),
                new SurfaceActionBinding(
                    "ino.delete", surface.SurfaceId, surface.SurfaceRevision, "ino.conversation.delete",
                    "digitalbrain.ino.conversation-delete.v1", "ui.action", 1, Hash("legacy-delete"),
                    1, 0, legacyExpiry, null, null)
            ]
        };

        var prepared = await feed.PrepareSessionAsync(context, CancellationToken.None);

        var binding = Assert.Single(prepared.State.ActionBindings);
        Assert.Equal(ConversationSurfacePayload.SendBindingId, binding.BindingId);
        Assert.Equal(ConversationSurfacePayload.SendActionType, binding.ActionType);
        Assert.True(prepared.ActionTokens.ContainsKey(ConversationSurfacePayload.SendBindingId));
        Assert.Equal(initial.State.LastSequence + 1, prepared.State.LastSequence);
        Assert.Equal(initial.State.EventHistory, prepared.State.EventHistory[..^1]);
        Assert.Equal(surface.SurfaceRevision + 1, Assert.Single(prepared.State.CurrentSurfaces).SurfaceRevision);
        var delivered = Assert.Single(feed.ReadPage(context, prepared.State, initial.State.LastSequence, 100).Items);
        Assert.Equal(ConversationSurfacePayload.SendBindingId, Assert.Single(delivered.Actions).BindingId);
        Assert.Equal(0, surfaceFeedNeuron.GenericProjectionCalls);
    }

    [Fact]
    public async Task PrepareSessionAsync_recreates_missing_terminal_actions_as_an_authoritative_event()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);
        var conversationNeuron = new FakeConversationNeuron(
            BuildConversationState(context, conversationId, revision: 1, assistantText: "Hello.", now));
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var clock = new MutableTimeProvider(now);
        var feed = new RuntimeSurfaceFeed(
            new FakeClusterClient(conversationNeuron, surfaceFeedNeuron),
            clock,
            ActionCapabilities(clock));
        var initial = await feed.PrepareSessionAsync(context, CancellationToken.None);
        var surface = Assert.Single(initial.State.CurrentSurfaces);
        surfaceFeedNeuron.Current = initial.State with { ActionBindings = [] };

        var repaired = await feed.PrepareSessionAsync(context, CancellationToken.None);

        var binding = Assert.Single(repaired.State.ActionBindings);
        Assert.Equal(ConversationSurfacePayload.SendBindingId, binding.BindingId);
        Assert.Contains(ConversationSurfacePayload.SendBindingId, repaired.ActionTokens.Keys);
        Assert.Equal(initial.State.LastSequence + 1, repaired.State.LastSequence);
        Assert.Equal(initial.State.EventHistory, repaired.State.EventHistory[..^1]);
        Assert.Equal(surface.SurfaceRevision + 1, Assert.Single(repaired.State.CurrentSurfaces).SurfaceRevision);
        var delivered = Assert.Single(feed.ReadPage(context, repaired.State, initial.State.LastSequence, 100).Items);
        Assert.Equal(ConversationSurfacePayload.SendBindingId, Assert.Single(delivered.Actions).BindingId);
        Assert.Equal(0, surfaceFeedNeuron.GenericProjectionCalls);
    }

    [Fact]
    public async Task PrepareSessionAsync_upgrades_the_complete_v1_presentation_with_an_authoritative_event()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);
        var conversationNeuron = new FakeConversationNeuron(
            BuildConversationState(context, conversationId, revision: 13, assistantText: "Hello.", now));
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var clock = new MutableTimeProvider(now);
        var feed = new RuntimeSurfaceFeed(
            new FakeClusterClient(conversationNeuron, surfaceFeedNeuron),
            clock,
            ActionCapabilities(clock));
        var initial = await feed.PrepareSessionAsync(context, CancellationToken.None);
        var surface = Assert.Single(initial.State.CurrentSurfaces);
        var presentation = JsonSerializer.Deserialize<SurfaceFeedPresentation>(surface.PayloadUtf8)!;
        var v1Surface = surface with
        {
            PayloadUtf8 = JsonSerializer.SerializeToUtf8Bytes(presentation with
            {
                ConversationRevision = 13,
                PresentationVersion = 1
            })
        };
        surfaceFeedNeuron.Current = initial.State with
        {
            CurrentSurfaces = [v1Surface],
            EventHistory = initial.State.EventHistory
                .Select(record => record.Sequence == v1Surface.Sequence ? v1Surface : record)
                .ToArray(),
            ActionBindings = []
        };

        var repaired = await feed.PrepareSessionAsync(context, CancellationToken.None);

        Assert.Equal(initial.State.LastSequence + 1, repaired.State.LastSequence);
        var upgraded = JsonSerializer.Deserialize<SurfaceFeedPresentation>(
            Assert.Single(repaired.State.CurrentSurfaces).PayloadUtf8);
        Assert.NotNull(upgraded);
        Assert.Equal(13, upgraded.ConversationRevision);
        Assert.Equal(SurfaceFeedPresentation.CurrentVersion, upgraded.PresentationVersion);
        Assert.Equal(ConversationSurfacePayload.SendBindingId, Assert.Single(repaired.State.ActionBindings).BindingId);
    }

    [Fact]
    public void Presentation_compatibility_rejects_a_v1_record_with_extra_top_level_fields()
    {
        var conversationId = "ino-" + new string('a', 64);
        var payload = ConversationSurfacePayload.Build(new InoConversationSnapshot(conversationId, 13, [], []));
        var presentation = new SurfaceFeedPresentation(
            "request-v1",
            "conversation",
            conversationId,
            ConversationSurfacePayload.RequiredCapabilities,
            payload,
            ConversationRevision: 13,
            PresentationVersion: 1);
        var exact = JsonSerializer.SerializeToElement(presentation);
        var extended = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            [nameof(SurfaceFeedPresentation.CorrelationId)] = presentation.CorrelationId,
            [nameof(SurfaceFeedPresentation.CauseKind)] = presentation.CauseKind,
            [nameof(SurfaceFeedPresentation.CauseId)] = presentation.CauseId,
            [nameof(SurfaceFeedPresentation.RequiredClientCapabilities)] = presentation.RequiredClientCapabilities,
            [nameof(SurfaceFeedPresentation.Payload)] = presentation.Payload,
            [nameof(SurfaceFeedPresentation.ConversationRevision)] = presentation.ConversationRevision,
            [nameof(SurfaceFeedPresentation.PresentationVersion)] = presentation.PresentationVersion,
            ["Unexpected"] = true
        });

        Assert.True(SurfaceFeedPresentationCompatibility.HasSupportedShape(exact, presentation));
        Assert.False(SurfaceFeedPresentationCompatibility.HasSupportedShape(extended, presentation));
    }

    [Fact]
    public async Task PrepareSessionAsync_activates_the_existing_conversation_for_durable_recovery()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);
        var conversationNeuron = new FakeConversationNeuron(
            BuildConversationState(context, conversationId, revision: 1, assistantText: "Hello.", now));
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var clock = new MutableTimeProvider(now);
        var feed = new RuntimeSurfaceFeed(
            new FakeClusterClient(conversationNeuron, surfaceFeedNeuron),
            clock,
            ActionCapabilities(clock));

        await feed.PrepareSessionAsync(context, CancellationToken.None);

        Assert.Equal(1, conversationNeuron.ReadCalls);
    }

    [Fact]
    public async Task AuthorizeActionAsync_reports_wrong_revision_when_an_obsolete_surface_binding_was_pruned()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);
        var conversationNeuron = new FakeConversationNeuron(
            BuildConversationState(context, conversationId, revision: 1, assistantText: "Hello.", now));
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var feed = new RuntimeSurfaceFeed(new FakeClusterClient(conversationNeuron, surfaceFeedNeuron), TimeProvider.System, ActionCapabilities());

        var prepared = await feed.PrepareSessionAsync(context, CancellationToken.None);
        var obsoleteSurface = prepared.State.CurrentSurfaces.Single(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        var obsoleteToken = prepared.ActionTokens[ConversationSurfacePayload.SendBindingId].Token;
        await surfaceFeedNeuron.ApplyProjectionAsync(
            prepared.State.Revision,
            new SurfaceFeedProjection(
                "prune-obsolete-send-binding",
                obsoleteSurface.SurfaceId,
                checked(obsoleteSurface.SurfaceRevision + 1),
                obsoleteSurface.ContentHash,
                obsoleteSurface.PayloadUtf8,
                now.AddSeconds(1),
                null,
                []),
            now.AddSeconds(1));

        var exception = await Assert.ThrowsAsync<ActionRejectedException>(() => feed.AuthorizeActionAsync(
            context,
            ConversationSurfacePayload.SendBindingId,
            obsoleteToken,
            obsoleteSurface.SurfaceId,
            obsoleteSurface.SurfaceRevision,
            JsonSerializer.SerializeToElement(new { prompt = "Hello" }),
            CancellationToken.None));

        Assert.Equal(ActionRejection.WrongRevision, exception.Reason);
    }

    [Fact]
    public async Task AuthorizeActionAsync_replays_the_same_signed_client_submission_without_a_second_consumption()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);
        var conversationNeuron = new FakeConversationNeuron(
            BuildConversationState(context, conversationId, revision: 1, assistantText: "Hello.", now));
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var feed = new RuntimeSurfaceFeed(new FakeClusterClient(conversationNeuron, surfaceFeedNeuron), TimeProvider.System, ActionCapabilities());

        var prepared = await feed.PrepareSessionAsync(context, CancellationToken.None);
        var surface = prepared.State.CurrentSurfaces.Single(candidate =>
            string.Equals(candidate.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        var token = prepared.ActionTokens[ConversationSurfacePayload.SendBindingId].Token;
        var input = JsonSerializer.SerializeToElement(new
        {
            prompt = "Hello",
            clientSubmissionId = "client-submission-000000000000000000000001"
        });

        var first = await feed.AuthorizeActionAsync(
            context,
            ConversationSurfacePayload.SendBindingId,
            token,
            surface.SurfaceId,
            surface.SurfaceRevision,
            input,
            CancellationToken.None);
        var replay = await feed.AuthorizeActionAsync(
            context,
            ConversationSurfacePayload.SendBindingId,
            token,
            surface.SurfaceId,
            surface.SurfaceRevision,
            input,
            CancellationToken.None);

        Assert.Equal(first.Submission.OperationId, replay.Submission.OperationId);
        Assert.Equal(1, surfaceFeedNeuron.Current.ActionBindings.Single(binding =>
            string.Equals(binding.BindingId, ConversationSurfacePayload.SendBindingId, StringComparison.Ordinal)).Uses);
    }

    [Fact]
    public async Task AuthorizeActionAsync_rejects_a_tampered_signed_capability_without_consuming_the_binding()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);
        var conversationNeuron = new FakeConversationNeuron(
            BuildConversationState(context, conversationId, revision: 1, assistantText: "Hello.", now));
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var feed = new RuntimeSurfaceFeed(new FakeClusterClient(conversationNeuron, surfaceFeedNeuron), TimeProvider.System, ActionCapabilities());

        var prepared = await feed.PrepareSessionAsync(context, CancellationToken.None);
        var surface = prepared.State.CurrentSurfaces.Single(candidate =>
            string.Equals(candidate.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        var token = prepared.ActionTokens[ConversationSurfacePayload.SendBindingId].Token;

        var exception = await Assert.ThrowsAsync<ActionRejectedException>(() => feed.AuthorizeActionAsync(
            context,
            ConversationSurfacePayload.SendBindingId,
            token[..^1] + (token[^1] == 'A' ? "B" : "A"),
            surface.SurfaceId,
            surface.SurfaceRevision,
            JsonSerializer.SerializeToElement(new { prompt = "Hello" }),
            CancellationToken.None));

        Assert.Equal(ActionRejection.Forged, exception.Reason);
        Assert.Equal(0, surfaceFeedNeuron.Current.ActionBindings.Single(binding =>
            string.Equals(binding.BindingId, ConversationSurfacePayload.SendBindingId, StringComparison.Ordinal)).Uses);
    }

    [Fact]
    public async Task AuthorizeActionAsync_treats_a_missing_current_binding_as_stale()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);
        var conversationNeuron = new FakeConversationNeuron(
            BuildConversationState(context, conversationId, revision: 1, assistantText: "Hello.", now));
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var feed = new RuntimeSurfaceFeed(new FakeClusterClient(conversationNeuron, surfaceFeedNeuron), TimeProvider.System, ActionCapabilities());

        var prepared = await feed.PrepareSessionAsync(context, CancellationToken.None);
        var previous = prepared.State.CurrentSurfaces.Single(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        var token = prepared.ActionTokens[ConversationSurfacePayload.SendBindingId].Token;
        var current = await surfaceFeedNeuron.ApplyProjectionAsync(
            prepared.State.Revision,
            new SurfaceFeedProjection(
                "remove-send-binding",
                previous.SurfaceId,
                checked(previous.SurfaceRevision + 1),
                previous.ContentHash,
                previous.PayloadUtf8,
                now.AddSeconds(1),
                null,
                []),
            now.AddSeconds(1));
        var currentSurface = current.CurrentSurfaces.Single(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));

        var exception = await Assert.ThrowsAsync<ActionRejectedException>(() => feed.AuthorizeActionAsync(
            context,
            ConversationSurfacePayload.SendBindingId,
            token,
            currentSurface.SurfaceId,
            currentSurface.SurfaceRevision,
            JsonSerializer.SerializeToElement(new { prompt = "Hello" }),
            CancellationToken.None));

        Assert.Equal(ActionRejection.WrongRevision, exception.Reason);
    }

    [Fact]
    public async Task AuthorizeActionAsync_treats_an_expired_binding_as_stale()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);
        var conversationNeuron = new FakeConversationNeuron(
            BuildConversationState(context, conversationId, revision: 1, assistantText: "Hello.", now));
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var feed = new RuntimeSurfaceFeed(
            new FakeClusterClient(conversationNeuron, surfaceFeedNeuron),
            clock,
            ActionCapabilities(clock));

        var prepared = await feed.PrepareSessionAsync(context, CancellationToken.None);
        var surface = prepared.State.CurrentSurfaces.Single(candidate =>
            string.Equals(candidate.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        var token = prepared.ActionTokens[ConversationSurfacePayload.SendBindingId].Token;
        clock.UtcNow = now.Add(UiProtocol.ActionTokenLifetime).AddSeconds(1);

        var exception = await Assert.ThrowsAsync<ActionRejectedException>(() => feed.AuthorizeActionAsync(
            context,
            ConversationSurfacePayload.SendBindingId,
            token,
            surface.SurfaceId,
            surface.SurfaceRevision,
            JsonSerializer.SerializeToElement(new { prompt = "Hello" }),
            CancellationToken.None));

        Assert.Equal(ActionRejection.WrongRevision, exception.Reason);
    }

    [Fact]
    public async Task AuthorizeActionAsync_rejects_an_approval_for_an_operation_not_rendered_by_the_signed_surface()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);
        var conversation = BuildAwaitingApprovalState(context, conversationId, now);
        var conversationNeuron = new FakeConversationNeuron(conversation);
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var feed = new RuntimeSurfaceFeed(new FakeClusterClient(conversationNeuron, surfaceFeedNeuron), clock, ActionCapabilities(clock));

        await SeedConversationPhaseAsync(
            surfaceFeedNeuron,
            context,
            ConversationStateClient.ToSnapshot(context with { ConversationId = conversationId }, conversation),
            "awaiting-approval-phase",
            now);

        var prepared = await feed.PrepareSessionAsync(context, CancellationToken.None);
        var surface = prepared.State.CurrentSurfaces.Single(candidate =>
            string.Equals(candidate.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        var approvalToken = prepared.ActionTokens[ConversationSurfacePayload.ApprovalBindingId].Token;

        var exception = await Assert.ThrowsAsync<ActionRejectedException>(() => feed.AuthorizeActionAsync(
            context,
            ConversationSurfacePayload.ApprovalBindingId,
            approvalToken,
            surface.SurfaceId,
            surface.SurfaceRevision,
            JsonSerializer.SerializeToElement(new
            {
                operationId = "operation-earlier",
                approvalId = "approval-earlier",
                decision = "approve",
                clientDecisionId = "ui-decision-000000000000000000000000"
            }),
            CancellationToken.None));

        Assert.Equal(ActionRejection.PolicyDenied, exception.Reason);
        Assert.Equal(0, surfaceFeedNeuron.Current.ActionBindings.Single(binding =>
            string.Equals(binding.BindingId, ConversationSurfacePayload.ApprovalBindingId, StringComparison.Ordinal)).Uses);
    }

    private static async Task SeedConversationPhaseAsync(
        FakeSurfaceFeedNeuron feed,
        RuntimeRequestContext context,
        InoConversationSnapshot conversation,
        string projectionId,
        DateTimeOffset now)
    {
        var state = feed.Current;
        if (state.Identity is null)
            state = await feed.InitializeAsync(
                state.Revision,
                new SurfaceFeedIdentity(context.OwnerId, context.ActorId));
        var descriptors = ConversationSurfacePayload.Actions(conversation, now);
        var payload = ConversationSurfacePayload.Build(conversation);
        var revision = checked((state.CurrentSurfaces.FirstOrDefault(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal))?.SurfaceRevision ?? 0) + 1);
        var bindings = descriptors.Select(descriptor => new SurfaceActionBinding(
            descriptor.BindingId,
            ConversationSurfacePayload.HomeSurfaceId,
            revision,
            descriptor.ActionType,
            descriptor.InputSchemaRef,
            descriptor.RequiredGrant,
            descriptor.ActionSchemaVersion,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(projectionId + "\0" + descriptor.BindingId))),
            descriptor.MaxUses,
            0,
            descriptor.ExpiresAt,
            null,
            null)).ToArray();
        var persisted = JsonSerializer.SerializeToUtf8Bytes(new
        {
            CorrelationId = "request-" + projectionId,
            CauseKind = "conversation",
            CauseId = conversation.ConversationId,
            RequiredClientCapabilities = ConversationSurfacePayload.RequiredCapabilities,
            Payload = payload,
            ConversationRevision = conversation.Revision,
            PresentationVersion = 2
        });
        await feed.ApplyProjectionAsync(
            state.Revision,
            new SurfaceFeedProjection(
                projectionId,
                ConversationSurfacePayload.HomeSurfaceId,
                revision,
                SurfaceContentHash.Compute(payload, descriptors),
                persisted,
                now,
                null,
                bindings),
            now);
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static RuntimeRequestContext Context() => new(
        new BrainOwnerId("owner"),
        new ActorId("principal"),
        new SessionId("session"),
        AuthAssurance.Oidc,
        "correlation",
        null,
        new HashSet<string>(["ui.action"], StringComparer.Ordinal));

    private static SessionTokenService ActionCapabilities(TimeProvider? timeProvider = null) =>
        new(Enumerable.Repeat((byte)3, 32).ToArray(), timeProvider ?? TimeProvider.System);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private static ConversationState BuildConversationState(
        RuntimeRequestContext context, string conversationId, long revision, string assistantText, DateTimeOffset now) => new(
        RuntimeStateSchemas.Conversation,
        revision,
        ConversationLifecycle.Active,
        new ConversationIdentity(context.OwnerId, context.ActorId, conversationId),
        [new ConversationTurn(1, "assistant", assistantText, now, "operation-1", ConversationTurnKind.Assistant, "operation-1")],
        [],
        [new ConversationOperation(
            "operation-1", "command-1", ConversationOperationStatus.Succeeded, 1, null, null, null,
            ConversationTerminalPolicy.NeverRetry, null, null, now)],
        [],
        null,
        null,
        []);

    private static ConversationState BuildAwaitingApprovalState(
        RuntimeRequestContext context,
        string conversationId,
        DateTimeOffset now) => new(
        RuntimeStateSchemas.Conversation,
        2,
        ConversationLifecycle.Active,
        new ConversationIdentity(context.OwnerId, context.ActorId, conversationId),
        [],
        [],
        [
            AwaitingApprovalOperation("operation-earlier", "approval-earlier", now),
            AwaitingApprovalOperation("operation-current", "approval-current", now)
        ],
        [],
        null,
        null,
        []);

    private static ConversationOperation AwaitingApprovalOperation(string operationId, string approvalId, DateTimeOffset now) => new(
        operationId,
        "command-" + operationId,
        ConversationOperationStatus.AwaitingApproval,
        1,
        null,
        null,
        null,
        ConversationTerminalPolicy.ManualIntervention,
        null,
        null,
        now,
        Version: 1,
        RequestId: "request-" + operationId,
        Approval: new ApprovalRecord(
            approvalId,
            operationId,
            "effect-" + operationId,
            "requested",
            1,
            now));

    private sealed class FakeConversationNeuron(ConversationState initial) : IConversationNeuron
    {
        public ConversationState Current { get; set; } = initial;
        public int ReadCalls { get; private set; }

        public Task<ConversationState> ReadAsync()
        {
            ReadCalls++;
            return Task.FromResult(Current);
        }

        public Task<ConversationArchivePage> ReadArchiveAsync(ConversationArchiveCursor? cursor, int maximumTurns) =>
            throw new NotSupportedException();
        public Task<ConversationState> InitializeAsync(long expectedRevision, ConversationIdentity identity) =>
            throw new NotSupportedException();
        public Task<ConversationState> BeginOperationAsync(
            long expectedRevision, string commandId, string inputHash, string operationId, string userText, string requestId,
            ConversationOutboxEntry acceptedOutbox, DateTimeOffset createdAt)
        {
            Current = ConversationTransitions.BeginOperation(
                Current,
                expectedRevision,
                commandId,
                inputHash,
                operationId,
                userText,
                requestId,
                acceptedOutbox,
                createdAt);
            return Task.FromResult(Current);
        }
        public Task<ConversationClaim> TryClaimOperationAsync(
            long expectedRevision, string operationId, string leaseOwner, DateTimeOffset now, TimeSpan leaseDuration,
            ConversationOutboxEntry? runningOutbox = null) =>
            throw new NotSupportedException();
        public Task<ConversationClaim> TryClaimAuthorizationAsync(
            long expectedRevision, string operationId, string authorizationAttemptId, string leaseOwner, DateTimeOffset now,
            TimeSpan leaseDuration, ConversationOutboxEntry? runningOutbox = null) =>
            throw new NotSupportedException();
        public Task<ConversationState> SuspendAuthorizationWithAssistantAsync(
            long expectedRevision, string operationId, SuspendedInvocation invocation, string assistantText,
            ConversationOutboxEntry feedOutbox, DateTimeOffset now, ConversationLeaseFence? leaseFence = null) =>
            throw new NotSupportedException();
        public Task<ConversationState> RequestApprovalWithAssistantAsync(
            long expectedRevision, string operationId, ApprovalRecord approval, EffectRecord effect,
            string assistantText, ConversationOutboxEntry feedOutbox, DateTimeOffset now,
            WorkflowReference? workflow = null, ConversationLeaseFence? leaseFence = null) =>
            throw new NotSupportedException();
        public Task<ConversationState> DecideApprovalWithAssistantAsync(
            long expectedRevision, string operationId, string approvalId, bool approved, string decisionId, string decidedBy,
            string assistantText, ConversationOutboxEntry feedOutbox, DateTimeOffset now) =>
            throw new NotSupportedException();
        public Task<ConversationState> ScheduleRetryAsync(
            long expectedRevision, string operationId, DateTimeOffset nextAttemptAt, string safeReason,
            DateTimeOffset now, ConversationOutboxEntry? retryOutbox = null, ConversationLeaseFence? leaseFence = null) =>
            throw new NotSupportedException();
        public Task<ConversationState> CompleteWithAssistantAsync(
            long expectedRevision, string operationId, ConversationOperationStatus terminalStatus,
            ConversationTerminalPolicy terminalPolicy, string? safeReason, string assistantText,
            ConversationOutboxEntry feedOutbox, DateTimeOffset now, WorkflowReference? workflow = null,
            ConversationLeaseFence? leaseFence = null) =>
            throw new NotSupportedException();
        public Task<ConversationState> CompleteEffectWithAssistantAsync(
            long expectedRevision, string operationId, EffectRecord effect, ConversationOperationStatus terminalStatus,
            ConversationTerminalPolicy terminalPolicy, string? safeReason, string assistantText,
            ConversationOutboxEntry feedOutbox, DateTimeOffset now, ConversationLeaseFence? leaseFence = null) =>
            throw new NotSupportedException();
        public Task<ConversationState> MarkOutboxDispatchedAsync(long expectedRevision, string outboxId, DateTimeOffset dispatchedAt) =>
            throw new NotSupportedException();
        public Task<ConversationState> RecordMigrationAsync(long expectedRevision, string migrationId) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSurfaceFeedNeuron(SurfaceFeedState initial) : ISurfaceFeedNeuron
    {
        public SurfaceFeedState Current { get; set; } = initial;
        public int GenericProjectionCalls { get; private set; }
        public int HomeSurfaceTransitions { get; private set; }

        public Task<SurfaceFeedState> ReadAsync() => Task.FromResult(Current);

        public Task<SurfaceFeedState> InitializeAsync(long expectedRevision, SurfaceFeedIdentity identity)
        {
            Current = SurfaceFeedTransitions.Initialize(Current, expectedRevision, identity);
            return Task.FromResult(Current);
        }

        public Task<SurfaceFeedState> EnsureHomeSurfaceAsync(long expectedRevision, HomeSurfaceBootstrap bootstrap)
        {
            HomeSurfaceTransitions++;
            Current = SurfaceFeedTransitions.EnsureHomeSurface(Current, expectedRevision, bootstrap);
            return Task.FromResult(Current);
        }

        public Task<SurfaceFeedState> ApplyProjectionAsync(long expectedRevision, SurfaceFeedProjection projection, DateTimeOffset now)
        {
            GenericProjectionCalls++;
            Current = SurfaceFeedTransitions.ApplyProjection(Current, expectedRevision, projection, now);
            return Task.FromResult(Current);
        }

        public Task<SurfaceFeedState> RecordDeliveryAsync(long expectedRevision, string deliveryId, long sequence, DateTimeOffset deliveredAt) =>
            throw new NotSupportedException();
        public Task<SurfaceFeedState> AcknowledgeAsync(
            long expectedRevision, string sessionScopeHash, long sequence, DateTimeOffset cursorExpiresAt, DateTimeOffset now) =>
            throw new NotSupportedException();
        public Task<SurfaceFeedState> RevokeSessionAsync(long expectedRevision, string sessionScopeHash, DateTimeOffset now) =>
            throw new NotSupportedException();
        public Task<SurfaceActionConsumption> ConsumeActionAsync(
            long expectedRevision, string bindingId, string tokenHash, string idempotencyKey, string operationId, DateTimeOffset now)
        {
            var consumption = SurfaceFeedTransitions.ConsumeAction(
                Current,
                expectedRevision,
                bindingId,
                tokenHash,
                idempotencyKey,
                operationId,
                now);
            Current = consumption.State;
            return Task.FromResult(consumption);
        }
        public Task<SurfaceFeedState> RenewActionBindingsAsync(long expectedRevision, DateTimeOffset now)
        {
            Current = SurfaceFeedTransitions.RenewActionBindings(Current, expectedRevision, now);
            return Task.FromResult(Current);
        }
        public Task<SurfaceFeedState> RebuildAsync(long expectedRevision, string projectionId, DateTimeOffset now) =>
            throw new NotSupportedException();
    }

    private sealed class FakeClusterClient(FakeConversationNeuron conversation, FakeSurfaceFeedNeuron surfaceFeed) : IClusterClient
    {
        public IServiceProvider ServiceProvider => throw new NotSupportedException();

        public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithStringKey
        {
            if (typeof(TGrainInterface) == typeof(IConversationNeuron)) return (TGrainInterface)(object)conversation;
            if (typeof(TGrainInterface) == typeof(ISurfaceFeedNeuron)) return (TGrainInterface)(object)surfaceFeed;
            throw new NotSupportedException($"Unexpected grain interface {typeof(TGrainInterface)}.");
        }

        public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string keyExtension, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithStringKey => throw new NotSupportedException();
        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithGuidKey => throw new NotSupportedException();
        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithIntegerKey => throw new NotSupportedException();
        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithGuidCompoundKey => throw new NotSupportedException();
        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithIntegerCompoundKey => throw new NotSupportedException();

        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey, string? keyExtension = null) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey, string? keyExtension = null) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey, string? keyExtension = null) => throw new NotSupportedException();

        public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) where TGrainInterface : IAddressable => throw new NotSupportedException();
        public IAddressable GetGrain(GrainId grainId) => throw new NotSupportedException();
        public IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType) => throw new NotSupportedException();
        public IAddressable GetGrain(Type interfaceType, IdSpan grainKey, string? grainClassNamePrefix = null) => throw new NotSupportedException();
        public IAddressable GetGrain(Type interfaceType, IdSpan grainKey) => throw new NotSupportedException();

        public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj)
            where TGrainObserverInterface : IGrainObserver => throw new NotSupportedException();
        public void DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj)
            where TGrainObserverInterface : IGrainObserver => throw new NotSupportedException();
    }
}
