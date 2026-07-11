extern alias McpProject;

using System.Text;
using System.Text.Json;
using DigitalBrain.Core.V2;
using V2InoEffectStore = McpProject::DigitalBrain.Mcp.V2InoEffectStore;
using V2McpConversationContextAssembler = McpProject::DigitalBrain.Mcp.V2McpConversationContextAssembler;
using V2McpInoCommandHandler = McpProject::DigitalBrain.Mcp.V2McpInoCommandHandler;
using V2McpNoToolCatalog = McpProject::DigitalBrain.Mcp.V2McpNoToolCatalog;
using V2McpNoToolPlanner = McpProject::DigitalBrain.Mcp.V2McpNoToolPlanner;
using V2McpResponseComposer = McpProject::DigitalBrain.Mcp.V2McpResponseComposer;
using V2RequestContext = DigitalBrain.Core.V2.RequestContext;

namespace DigitalBrain.Tests.V2;

public sealed class V2InoConversationTests
{
    private const string Prompt = "What can you help me with in this workspace?";

    [Fact]
    public async Task Command_executes_the_real_owner_after_user_persistence_and_publishes_ordered_principal_states()
    {
        var context = Context();
        var feed = new V2PrivateFeedStore();
        var store = new V2InoEffectStore();
        var actions = new V2ActionExecutor(feed);
        var surfaces = new V2WorkspaceSurfaceProducer(feed, actions, store);
        var application = new V2ApplicationService();
        V2OperationStatus? submitted = null;
        var router = new RecordingModelRouter(async request =>
        {
            var duringModel = store.Read(context);
            Assert.Single(duringModel.Turns);
            Assert.Equal("user", duringModel.Turns[0].Role);
            Assert.Equal(Prompt, duringModel.Turns[0].Text);
            Assert.Equal(V2InoConversationStates.Responding, duringModel.CurrentOperation!.State);
            Assert.Equal(WorkflowState.Applying,
                (await application.GetOperationAsync(context, submitted!.OperationId))!.State);
            return new V2ModelResponse($"I can help you explore: {request.Text}", "test-model", false);
        });
        var handler = Handler(store, surfaces, router);
        var dispatcher = new V2CommandDispatcher(application, [handler]);
        var command = new V2CommandEnvelope(V2McpInoCommandHandler.CommandType, 2, "ino-command", context,
            JsonSerializer.SerializeToElement(new { prompt = Prompt }));

        submitted = await application.SubmitAsync(context, command);
        Assert.True(await dispatcher.DispatchAsync(submitted.OperationId));

        var completed = await application.GetOperationAsync(context, submitted.OperationId);
        Assert.Equal(WorkflowState.Succeeded, completed!.State);
        Assert.Equal(1, router.Calls);
        var conversation = store.Read(context);
        Assert.Equal(V2InoConversationStates.Succeeded, conversation.CurrentOperation!.State);
        Assert.Collection(conversation.Turns,
            user =>
            {
                Assert.Equal("user", user.Role);
                Assert.Equal(Prompt, user.Text);
                Assert.Equal(V2InoConversationStates.Succeeded, user.State);
            },
            assistant =>
            {
                Assert.Equal("assistant", assistant.Role);
                Assert.Contains(Prompt, assistant.Text, StringComparison.Ordinal);
                Assert.Equal(V2InoConversationStates.Succeeded, assistant.State);
            });

        var principalItems = feed.CatchUp(context, V2SurfaceAudienceKind.Principal, 0).Items;
        Assert.Equal(
            new[] { V2InoConversationStates.Queued, V2InoConversationStates.Running,
                    V2InoConversationStates.Responding, V2InoConversationStates.Succeeded },
            principalItems.Select(OperationState).ToArray());
        Assert.Empty(feed.CatchUp(context, V2SurfaceAudienceKind.Workspace, 0).Items);
        var rendered = principalItems[^1].Payload.GetRawText();
        Assert.DoesNotContain(context.TenantId.Value, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(context.WorkspaceId.Value, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(context.Principal.Value, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(submitted.OperationId, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(command.CommandId, rendered, StringComparison.Ordinal);
        Assert.All(
            principalItems[^1].Payload.GetProperty("data").GetProperty("messages").EnumerateArray(),
            message => Assert.Matches("^turn-[a-f0-9]{24}$", message.GetProperty("turnKey").GetString()!));
    }

    [Fact]
    public async Task Conversation_and_feed_are_isolated_by_principal_value_and_kind()
    {
        var owner = Context();
        var otherPrincipal = owner with
        {
            Principal = new PrincipalRef("other-user", PrincipalKind.User),
            SessionId = "other-session"
        };
        var otherKind = owner with
        {
            Principal = new PrincipalRef(owner.Principal.Value, PrincipalKind.Service),
            SessionId = "service-session"
        };
        var feed = new V2PrivateFeedStore();
        var store = new V2InoEffectStore();
        var surfaces = new V2WorkspaceSurfaceProducer(feed, new V2ActionExecutor(feed), store);
        var router = new RecordingModelRouter(request => Task.FromResult(
            new V2ModelResponse("A private answer to: " + request.Text, "test-model", false)));

        var result = await Handler(store, surfaces, router).ExecuteAsync(new V2CommandEnvelope(
            V2McpInoCommandHandler.CommandType, 2, "owner-command", owner,
            JsonSerializer.SerializeToElement(new { prompt = Prompt })));

        Assert.Equal(WorkflowState.Succeeded, result.State);
        Assert.Equal(2, store.Read(owner).Turns.Count);
        Assert.Empty(store.Read(otherPrincipal).Turns);
        Assert.Empty(store.Read(otherKind).Turns);
        Assert.Empty(feed.CatchUp(otherPrincipal, V2SurfaceAudienceKind.Principal, 0).Items);
        Assert.Empty(feed.CatchUp(otherKind, V2SurfaceAudienceKind.Principal, 0).Items);
    }

    [Fact]
    public async Task Model_failure_is_persisted_and_published_as_a_safe_non_retryable_terminal_state()
    {
        var context = Context();
        var feed = new V2PrivateFeedStore();
        var store = new V2InoEffectStore();
        var surfaces = new V2WorkspaceSurfaceProducer(feed, new V2ActionExecutor(feed), store);
        var router = new RecordingModelRouter(_ => throw new InvalidOperationException(
            "https://internal.invalid secret-token infrastructure detail"));

        var result = await Handler(store, surfaces, router).ExecuteAsync(new V2CommandEnvelope(
            V2McpInoCommandHandler.CommandType, 2, "failed-command", context,
            JsonSerializer.SerializeToElement(new { prompt = Prompt })));

        Assert.Equal(WorkflowState.Failed, result.State);
        var snapshot = store.Read(context);
        Assert.Single(snapshot.Turns);
        Assert.Equal(V2InoConversationStates.Failed, snapshot.Turns[0].State);
        Assert.False(snapshot.CurrentOperation!.Retryable);
        var terminal = feed.CatchUp(context, V2SurfaceAudienceKind.Principal, 0).Items[^1];
        var payload = terminal.Payload.GetRawText();
        Assert.Contains("I couldn’t finish that response",
            terminal.Payload.GetProperty("data").GetProperty("operation").GetProperty("safeReason").GetString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain("internal.invalid", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsafe_model_output_is_rejected_before_assistant_persistence_without_leaking_details()
    {
        var context = Context();
        var feed = new V2PrivateFeedStore();
        var store = new V2InoEffectStore();
        var surfaces = new V2WorkspaceSurfaceProducer(feed, new V2ActionExecutor(feed), store);
        var unsafeAnswer = $"Use https://internal.invalid/api with operationId op-42 for {context.WorkspaceId.Value}.";
        var router = new RecordingModelRouter(_ => Task.FromResult(
            new V2ModelResponse(unsafeAnswer, "test-model", false)));

        var result = await Handler(store, surfaces, router).ExecuteAsync(new V2CommandEnvelope(
            V2McpInoCommandHandler.CommandType, 2, "unsafe-answer-command", context,
            JsonSerializer.SerializeToElement(new { prompt = Prompt })));

        Assert.Equal(WorkflowState.Failed, result.State);
        var snapshot = store.Read(context);
        var user = Assert.Single(snapshot.Turns);
        Assert.Equal("user", user.Role);
        Assert.Equal(V2InoConversationStates.Failed, user.State);
        Assert.Equal(V2InoConversationStates.Failed, snapshot.CurrentOperation!.State);
        var terminal = feed.CatchUp(context, V2SurfaceAudienceKind.Principal, 0).Items[^1];
        var payload = terminal.Payload.GetRawText();
        Assert.DoesNotContain("internal.invalid", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("operationId", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(context.WorkspaceId.Value, payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "I couldn’t finish that response",
            terminal.Payload.GetProperty("data").GetProperty("operation").GetProperty("safeReason").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Presentation_policy_rejects_scheme_agnostic_service_uris()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new V2McpResponseComposer().ComposeAsync(
            Context(),
            new V2ModelResponse("Connect through tcp://worker before continuing.", "test-model", false),
            []));
    }

    [Fact]
    public async Task Presentation_policy_rejects_general_hostnames_with_ports()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new V2McpResponseComposer().ComposeAsync(
            Context(),
            new V2ModelResponse("Connect to build-worker:5000 before continuing.", "test-model", false),
            []));
    }

    [Theory]
    [InlineData("Open docs.example.ai before continuing.")]
    [InlineData("Connect to 2001:db8::1 before continuing.")]
    [InlineData(@"Read from \\fileserver\share before continuing.")]
    public async Task Presentation_policy_rejects_additional_endpoint_forms(string answer)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new V2McpResponseComposer().ComposeAsync(
            Context(),
            new V2ModelResponse(answer, "test-model", false),
            []));
    }

    [Fact]
    public async Task Presentation_policy_rejects_exact_authenticated_grant_values()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new V2McpResponseComposer().ComposeAsync(
            Context(),
            new V2ModelResponse("This session permits brain.read.", "test-model", false),
            []));
    }

    [Theory]
    [InlineData("tenant", "acme")]
    [InlineData("workspace", "local")]
    [InlineData("principal", "dev")]
    public async Task Presentation_policy_rejects_short_scope_values_when_labeled_as_identity(
        string label,
        string value)
    {
        var context = Context() with
        {
            TenantId = new TenantId("acme"),
            WorkspaceId = new WorkspaceId("local"),
            Principal = new PrincipalRef("dev", PrincipalKind.User)
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new V2McpResponseComposer().ComposeAsync(
            context,
            new V2ModelResponse($"Authenticated {label}: {value}.", "test-model", false),
            []));
    }

    [Theory]
    [InlineData("The workspace named local is ready.")]
    [InlineData("The workspace 'local' is ready.")]
    public async Task Presentation_policy_rejects_named_or_quoted_short_scope_values(string answer)
    {
        var context = Context() with
        {
            WorkspaceId = new WorkspaceId("local")
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new V2McpResponseComposer().ComposeAsync(
            context,
            new V2ModelResponse(answer, "test-model", false),
            []));
    }

    [Fact]
    public async Task Presentation_policy_does_not_reject_ordinary_local_or_default_language()
    {
        var context = Context() with
        {
            TenantId = new TenantId("local"),
            WorkspaceId = new WorkspaceId("default"),
            Principal = new PrincipalRef("flutter-ui", PrincipalKind.User)
        };
        const string answer = "I can help with this workspace, local files, and sensible defaults. We can check back at 12:30.";

        var composed = await new V2McpResponseComposer().ComposeAsync(
            context,
            new V2ModelResponse(answer, "test-model", false),
            []);

        Assert.Equal(answer, composed);
    }

    [Fact]
    public async Task Long_unicode_answers_are_fitted_and_old_completed_turns_are_pruned_before_publication()
    {
        var context = Context();
        var feed = new V2PrivateFeedStore();
        var store = new V2InoEffectStore();
        var surfaces = new V2WorkspaceSurfaceProducer(feed, new V2ActionExecutor(feed), store);
        var longAnswer = string.Concat(Enumerable.Repeat("🧠", 20_000));
        var router = new RecordingModelRouter(_ => Task.FromResult(
            new V2ModelResponse(longAnswer, "test-model", false)));
        var handler = Handler(store, surfaces, router);

        for (var index = 0; index < 2; index++)
        {
            var result = await handler.ExecuteAsync(new V2CommandEnvelope(
                V2McpInoCommandHandler.CommandType, 2, $"long-answer-{index}", context,
                JsonSerializer.SerializeToElement(new { prompt = $"Help me understand item {index}." })));
            Assert.Equal(WorkflowState.Succeeded, result.State);
        }

        var snapshot = store.Read(context);
        Assert.DoesNotContain(snapshot.Turns, turn => turn.CommandId == "long-answer-0");
        Assert.DoesNotContain(snapshot.Operations, operation => operation.CommandId == "long-answer-0");
        Assert.Collection(snapshot.Turns,
            user =>
            {
                Assert.Equal("long-answer-1", user.CommandId);
                Assert.Equal("user", user.Role);
                Assert.Equal(V2InoConversationStates.Succeeded, user.State);
            },
            assistant =>
            {
                Assert.Equal("long-answer-1", assistant.CommandId);
                Assert.Equal("assistant", assistant.Role);
                Assert.Equal(V2InoConversationStates.Succeeded, assistant.State);
                Assert.True(assistant.Text.EndsWith('…'));
                Assert.DoesNotContain("�", assistant.Text, StringComparison.Ordinal);
            });

        var payload = V2WorkspaceSurfaceProducer.BuildInoPayload(snapshot);
        var payloadBytes = Encoding.UTF8.GetByteCount(payload.GetRawText());
        Assert.InRange(payloadBytes, 1, V2WorkspaceSurfaceProducer.InoPayloadBudgetBytes);
        Assert.True(payloadBytes < V2PrivateFeedStore.MaximumSurfacePayloadBytes);
        var terminal = feed.CatchUp(context, V2SurfaceAudienceKind.Principal, 0).Items[^1];
        Assert.Equal(payload.GetRawText(), terminal.Payload.GetRawText());
    }

    [Fact]
    public async Task Restart_restores_the_transcript_and_direct_replay_cannot_duplicate_turns()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-ino-conversation-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = Context();
            var conversationPath = Path.Combine(root, "conversation.jsonl");
            var feedPath = Path.Combine(root, "feed.jsonl");
            var firstStore = new V2InoEffectStore(conversationPath);
            var firstFeed = new V2PrivateFeedStore(feedPath);
            var firstSurfaces = new V2WorkspaceSurfaceProducer(firstFeed, new V2ActionExecutor(firstFeed), firstStore);
            var firstRouter = new RecordingModelRouter(request => Task.FromResult(
                new V2ModelResponse("A durable answer to: " + request.Text, "test-model", false)));
            var command = new V2CommandEnvelope(V2McpInoCommandHandler.CommandType, 2, "stable-command", context,
                JsonSerializer.SerializeToElement(new { prompt = Prompt }));

            Assert.Equal(WorkflowState.Succeeded,
                (await Handler(firstStore, firstSurfaces, firstRouter).ExecuteAsync(command)).State);

            var journal = File.ReadAllLines(conversationPath)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                Assert.Equal(4, journal.Length);
                var respondingPersisted = journal[^2].RootElement.GetProperty("Snapshot");
                Assert.Equal(V2InoConversationStates.Responding,
                    respondingPersisted.GetProperty("Operations")[0].GetProperty("State").GetString());
                Assert.DoesNotContain(respondingPersisted.GetProperty("Turns").EnumerateArray(), turn =>
                    turn.GetProperty("Role").GetString() == "assistant");
                var completed = journal[^1].RootElement.GetProperty("Snapshot");
                Assert.Equal(V2InoConversationStates.Succeeded,
                    completed.GetProperty("Operations")[0].GetProperty("State").GetString());
                Assert.Contains(completed.GetProperty("Turns").EnumerateArray(), turn =>
                    turn.GetProperty("Role").GetString() == "assistant" &&
                    turn.GetProperty("State").GetString() == V2InoConversationStates.Succeeded);
            }
            finally
            {
                foreach (var document in journal) document.Dispose();
            }

            var reopenedStore = new V2InoEffectStore(conversationPath);
            var reopenedFeed = new V2PrivateFeedStore(feedPath);
            var reopenedSurfaces = new V2WorkspaceSurfaceProducer(
                reopenedFeed, new V2ActionExecutor(reopenedFeed), reopenedStore);
            var replayRouter = new RecordingModelRouter(_ => throw new InvalidOperationException("Replay called the model."));
            var restored = reopenedSurfaces.EnsureInitial(context, V2SurfaceAudienceKind.Principal);

            Assert.Equal(2, MessageCount(restored));
            Assert.Equal(WorkflowState.Succeeded,
                (await Handler(reopenedStore, reopenedSurfaces, replayRouter).ExecuteAsync(command)).State);
            Assert.Equal(0, replayRouter.Calls);
            Assert.Equal(2, reopenedStore.Read(context).Turns.Count);
            Assert.Equal(2, MessageCount(reopenedFeed.CatchUp(
                context, V2SurfaceAudienceKind.Principal, long.MaxValue).Items.Single()));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Complete_journals_response_action_and_all_groundings_atomically_and_replay_is_exact()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-ino-atomic-complete-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = Context();
            var path = Path.Combine(root, "conversation.jsonl");
            var store = new V2InoEffectStore(path);
            const string commandId = "atomic-command";
            store.Begin(context, commandId, Prompt);
            store.Transition(context, commandId, V2InoConversationStates.Running);
            store.Transition(context, commandId, V2InoConversationStates.Responding);
            var recordsBeforeComplete = File.ReadAllLines(path).Length;
            var action = new V2ToolAction("openUrl", "Connect Google", "https://accounts.google.com/");
            V2ToolGrounding[] groundings =
            [
                new("gmail.read.messages", JsonSerializer.SerializeToElement(new { messageId = "message-1" })),
                new("salesforce.read.records", JsonSerializer.SerializeToElement(new { recordId = "record-1" }))
            ];

            var completed = store.Complete(
                context,
                commandId,
                "Atomic grounded answer.",
                action,
                groundings[0],
                groundings);

            Assert.Equal(recordsBeforeComplete + 1, File.ReadAllLines(path).Length);
            var operation = Assert.Single(completed.Operations);
            Assert.Equal(V2InoConversationStates.Succeeded, operation.State);
            Assert.Equal(action, operation.Action);
            Assert.Equal(groundings[0].Content.GetRawText(), operation.Grounding!.Content.GetRawText());
            Assert.Equal(2, operation.Groundings!.Count);
            Assert.Equal("Atomic grounded answer.", Assert.Single(
                completed.Turns, static turn => turn.Role == "assistant").Text);

            var reopened = new V2InoEffectStore(path);
            var recordsBeforeReplay = File.ReadAllLines(path).Length;
            var replay = reopened.Complete(
                context,
                commandId,
                "Atomic grounded answer.",
                action,
                groundings[0],
                groundings);

            Assert.Equal(recordsBeforeReplay, File.ReadAllLines(path).Length);
            Assert.Equal(completed.Revision, replay.Revision);
            var replayOperation = Assert.Single(replay.Operations);
            Assert.Equal(action, replayOperation.Action);
            Assert.Equal(2, replayOperation.Groundings!.Count);
            Assert.Equal("Atomic grounded answer.", Assert.Single(
                replay.Turns, static turn => turn.Role == "assistant").Text);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Interrupted_legacy_partial_answer_recovers_failed_and_replay_never_claims_success()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-ino-interrupted-answer-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = Context();
            var path = Path.Combine(root, "conversation.jsonl");
            var store = new V2InoEffectStore(path);
            const string commandId = "interrupted-command";
            store.Begin(context, commandId, Prompt);
            store.Transition(context, commandId, V2InoConversationStates.Running);
            store.Transition(context, commandId, V2InoConversationStates.Responding);
            var responding = store.Read(context);
            var legacyPartial = responding with
            {
                Revision = checked(responding.Revision + 1),
                Turns = responding.Turns.Concat([
                    new V2InoConversationTurn(
                        commandId,
                        "assistant",
                        "This answer was never atomically committed.",
                        V2InoConversationStates.Responding)
                ]).ToArray()
            };
            File.AppendAllText(path, JsonSerializer.Serialize(new
            {
                Version = 3,
                Tenant = context.TenantId,
                Workspace = context.WorkspaceId,
                Principal = context.Principal,
                Snapshot = legacyPartial
            }) + Environment.NewLine);

            var recoveredStore = new V2InoEffectStore(path);
            var recovered = recoveredStore.Read(context);
            var recoveredOperation = Assert.Single(recovered.Operations);
            Assert.Equal(V2InoConversationStates.Failed, recoveredOperation.State);
            Assert.Contains("couldn’t confirm", recoveredOperation.SafeReason, StringComparison.Ordinal);
            Assert.Null(recoveredOperation.Action);
            Assert.Null(recoveredOperation.Grounding);
            Assert.Null(recoveredOperation.Groundings);
            var recoveredTurn = Assert.Single(recovered.Turns);
            Assert.Equal("user", recoveredTurn.Role);
            Assert.Equal(V2InoConversationStates.Failed, recoveredTurn.State);

            var feed = new V2PrivateFeedStore();
            var surfaces = new V2WorkspaceSurfaceProducer(feed, new V2ActionExecutor(feed), recoveredStore);
            var replayRouter = new RecordingModelRouter(_ => throw new InvalidOperationException("Replay called the model."));
            var command = new V2CommandEnvelope(
                V2McpInoCommandHandler.CommandType,
                2,
                commandId,
                context,
                JsonSerializer.SerializeToElement(new { prompt = Prompt }));

            var replay = await Handler(recoveredStore, surfaces, replayRouter).ExecuteAsync(command);

            Assert.Equal(WorkflowState.Failed, replay.State);
            Assert.Equal(0, replayRouter.Calls);
            Assert.Equal(V2InoConversationStates.Failed, recoveredStore.Read(context).CurrentOperation!.State);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Ino_action_exact_replay_converges_and_changed_prompt_conflicts()
    {
        var context = Context();
        var feed = new V2PrivateFeedStore();
        var actions = new V2ActionExecutor(feed);
        var surface = new V2WorkspaceSurfaceProducer(feed, actions).EnsureInitial(
            context, V2SurfaceAudienceKind.Principal);
        var binding = Assert.Single(surface.Actions);
        Assert.Equal(V2WorkspaceSurfaceProducer.InoBindingId, binding.BindingId);
        Assert.Equal(V2WorkspaceSurfaceProducer.InoActionType, binding.ActionType);
        var issued = actions.Issue(context, surface, binding, TimeSpan.FromMinutes(1));
        var application = new V2ApplicationService();
        var input = JsonSerializer.SerializeToElement(new { prompt = Prompt });

        var firstAuthorization = await actions.ReserveAsync(context, issued.BindingId, issued.Token,
            surface.SurfaceId, surface.Revision, input, CancellationToken.None);
        var firstContext = context with
        {
            IdempotencyKey = firstAuthorization.Submission.IdempotencyKey,
            Grants = context.Grants.Append("brain.act").ToHashSet(StringComparer.Ordinal)
        };
        var first = await application.SubmitAsync(firstContext, new V2CommandEnvelope(
            firstAuthorization.Submission.ActionType, 2, "first-command", firstContext, input));
        Assert.True(actions.Commit(firstAuthorization, first.OperationId));

        var replayAuthorization = await actions.ReserveAsync(context, issued.BindingId, issued.Token,
            surface.SurfaceId, surface.Revision, input, CancellationToken.None);
        var replayContext = firstContext with { IdempotencyKey = replayAuthorization.Submission.IdempotencyKey };
        var replay = await application.SubmitAsync(replayContext, new V2CommandEnvelope(
            replayAuthorization.Submission.ActionType, 2, "replay-command", replayContext, input));
        Assert.True(actions.Commit(replayAuthorization, replay.OperationId));
        Assert.Equal(first.OperationId, replay.OperationId);

        var changedInput = JsonSerializer.SerializeToElement(new { prompt = "A changed prompt" });
        var changedAuthorization = await actions.ReserveAsync(context, issued.BindingId, issued.Token,
            surface.SurfaceId, surface.Revision, changedInput, CancellationToken.None);
        var changedContext = firstContext with { IdempotencyKey = changedAuthorization.Submission.IdempotencyKey };
        await Assert.ThrowsAsync<V2IdempotencyConflictException>(() => application.SubmitAsync(
            changedContext, new V2CommandEnvelope(changedAuthorization.Submission.ActionType, 2,
                "changed-command", changedContext, changedInput)));
        actions.Release(changedAuthorization);
    }

    private static V2McpInoCommandHandler Handler(
        IV2InoConversationStore store,
        V2WorkspaceSurfaceProducer surfaces,
        IV2ModelRouter router) =>
        new(store, surfaces, new V2ConversationOwner(
            new V2McpConversationContextAssembler(store),
            new V2McpNoToolPlanner(),
            router,
            new V2McpNoToolCatalog(),
            new V2McpResponseComposer()));

    private static V2RequestContext Context() => new(
        new TenantId("private-tenant"),
        new WorkspaceId("private-workspace"),
        new PrincipalRef("private-user", PrincipalKind.User),
        "session",
        AuthAssurance.Password,
        "correlation",
        "exact-idempotency",
        new HashSet<string> { "brain.read", "brain.act", "ui.action" });

    private static string OperationState(V2StoredSurfaceRecord record) =>
        record.Payload.GetProperty("data").GetProperty("operation").GetProperty("state").GetString()!;

    private static int MessageCount(V2StoredSurfaceRecord record) =>
        record.Payload.GetProperty("data").GetProperty("messages").GetArrayLength();

    private sealed class RecordingModelRouter(
        Func<V2ModelRequest, Task<V2ModelResponse>> complete) : IV2ModelRouter
    {
        public int Calls { get; private set; }

        public Task<V2ModelResponse> CompleteAsync(
            V2ModelRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return complete(request);
        }
    }
}
