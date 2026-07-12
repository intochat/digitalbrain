extern alias McpProject;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Core.Runtime;
using InoEffectStore = McpProject::DigitalBrain.Mcp.InoEffectStore;
using McpConversationContextAssembler = McpProject::DigitalBrain.Mcp.McpConversationContextAssembler;
using McpInoCommandHandler = McpProject::DigitalBrain.Mcp.McpInoCommandHandler;
using McpNoToolCatalog = McpProject::DigitalBrain.Mcp.McpNoToolCatalog;
using McpNoToolPlanner = McpProject::DigitalBrain.Mcp.McpNoToolPlanner;
using McpResponseComposer = McpProject::DigitalBrain.Mcp.McpResponseComposer;
using ToolActionPolicy = McpProject::DigitalBrain.Mcp.ToolActionPolicy;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Tests.Runtime;

public sealed class InoConversationTests
{
    private const string InoJournalDomain = "digitalbrain.v2.ino-effects";
    private const string Prompt = "What can you help me with in this workspace?";

    [Fact]
    public async Task Command_executes_the_real_owner_after_user_persistence_and_publishes_ordered_principal_states()
    {
        var context = Context();
        var feed = new PrivateFeedStore();
        var store = new InoEffectStore();
        var actions = new ActionExecutor(feed);
        var surfaces = new WorkspaceSurfaceProducer(feed, actions, store);
        var application = new ApplicationService();
        OperationStatus? submitted = null;
        var router = new RecordingModelRouter(async request =>
        {
            var duringModel = store.Read(context);
            Assert.Single(duringModel.Turns);
            Assert.Equal("user", duringModel.Turns[0].Role);
            Assert.Equal(Prompt, duringModel.Turns[0].Text);
            Assert.Equal(InoConversationStates.Responding, duringModel.CurrentOperation!.State);
            Assert.Equal(WorkflowState.Applying,
                (await application.GetOperationAsync(context, submitted!.OperationId))!.State);
            return new ModelResponse($"I can help you explore: {request.Text}", "test-model", false);
        });
        var handler = Handler(store, surfaces, router);
        var dispatcher = new CommandDispatcher(application, [handler]);
        var command = new CommandEnvelope(McpInoCommandHandler.CommandType, 2, "ino-command", context,
            JsonSerializer.SerializeToElement(new { prompt = Prompt }));

        submitted = await application.SubmitAsync(context, command);
        Assert.True(await dispatcher.DispatchAsync(submitted.OperationId));

        var completed = await application.GetOperationAsync(context, submitted.OperationId);
        Assert.Equal(WorkflowState.Succeeded, completed!.State);
        Assert.Equal(1, router.Calls);
        var conversation = store.Read(context);
        Assert.Equal(InoConversationStates.Succeeded, conversation.CurrentOperation!.State);
        Assert.Collection(conversation.Turns,
            user =>
            {
                Assert.Equal("user", user.Role);
                Assert.Equal(Prompt, user.Text);
                Assert.Equal(InoConversationStates.Succeeded, user.State);
            },
            assistant =>
            {
                Assert.Equal("assistant", assistant.Role);
                Assert.Contains(Prompt, assistant.Text, StringComparison.Ordinal);
                Assert.Equal(InoConversationStates.Succeeded, assistant.State);
            });

        var principalItems = feed.CatchUp(context, SurfaceAudienceKind.Principal, 0).Items;
        Assert.Equal(
            new[] { InoConversationStates.Queued, InoConversationStates.Running,
                    InoConversationStates.Responding, InoConversationStates.Succeeded },
            principalItems.Select(OperationState).ToArray());
        Assert.Empty(feed.CatchUp(context, SurfaceAudienceKind.Workspace, 0).Items);
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
        var feed = new PrivateFeedStore();
        var store = new InoEffectStore();
        var surfaces = new WorkspaceSurfaceProducer(feed, new ActionExecutor(feed), store);
        var router = new RecordingModelRouter(request => Task.FromResult(
            new ModelResponse("A private answer to: " + request.Text, "test-model", false)));

        var result = await Handler(store, surfaces, router).ExecuteAsync(new CommandEnvelope(
            McpInoCommandHandler.CommandType, 2, "owner-command", owner,
            JsonSerializer.SerializeToElement(new { prompt = Prompt })));

        Assert.Equal(WorkflowState.Succeeded, result.State);
        Assert.Equal(2, store.Read(owner).Turns.Count);
        Assert.Empty(store.Read(otherPrincipal).Turns);
        Assert.Empty(store.Read(otherKind).Turns);
        Assert.Empty(feed.CatchUp(otherPrincipal, SurfaceAudienceKind.Principal, 0).Items);
        Assert.Empty(feed.CatchUp(otherKind, SurfaceAudienceKind.Principal, 0).Items);
    }

    [Fact]
    public async Task Model_failure_is_persisted_and_published_as_a_safe_non_retryable_terminal_state()
    {
        var context = Context();
        var feed = new PrivateFeedStore();
        var store = new InoEffectStore();
        var surfaces = new WorkspaceSurfaceProducer(feed, new ActionExecutor(feed), store);
        var router = new RecordingModelRouter(_ => throw new InvalidOperationException(
            "https://internal.invalid secret-token infrastructure detail"));

        var result = await Handler(store, surfaces, router).ExecuteAsync(new CommandEnvelope(
            McpInoCommandHandler.CommandType, 2, "failed-command", context,
            JsonSerializer.SerializeToElement(new { prompt = Prompt })));

        Assert.Equal(WorkflowState.Failed, result.State);
        var snapshot = store.Read(context);
        Assert.Single(snapshot.Turns);
        Assert.Equal(InoConversationStates.Failed, snapshot.Turns[0].State);
        Assert.False(snapshot.CurrentOperation!.Retryable);
        var terminal = feed.CatchUp(context, SurfaceAudienceKind.Principal, 0).Items[^1];
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
        var feed = new PrivateFeedStore();
        var store = new InoEffectStore();
        var surfaces = new WorkspaceSurfaceProducer(feed, new ActionExecutor(feed), store);
        var unsafeAnswer = $"Use https://internal.invalid/api with operationId op-42 for {context.WorkspaceId.Value}.";
        var router = new RecordingModelRouter(_ => Task.FromResult(
            new ModelResponse(unsafeAnswer, "test-model", false)));

        var result = await Handler(store, surfaces, router).ExecuteAsync(new CommandEnvelope(
            McpInoCommandHandler.CommandType, 2, "unsafe-answer-command", context,
            JsonSerializer.SerializeToElement(new { prompt = Prompt })));

        Assert.Equal(WorkflowState.Failed, result.State);
        var snapshot = store.Read(context);
        var user = Assert.Single(snapshot.Turns);
        Assert.Equal("user", user.Role);
        Assert.Equal(InoConversationStates.Failed, user.State);
        Assert.Equal(InoConversationStates.Failed, snapshot.CurrentOperation!.State);
        var terminal = feed.CatchUp(context, SurfaceAudienceKind.Principal, 0).Items[^1];
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => new McpResponseComposer().ComposeAsync(
            Context(),
            new ModelResponse("Connect through tcp://worker before continuing.", "test-model", false),
            []));
    }

    [Fact]
    public async Task Presentation_policy_rejects_general_hostnames_with_ports()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new McpResponseComposer().ComposeAsync(
            Context(),
            new ModelResponse("Connect to build-worker:5000 before continuing.", "test-model", false),
            []));
    }

    [Theory]
    [InlineData("Open docs.example.ai before continuing.")]
    [InlineData("Connect to 2001:db8::1 before continuing.")]
    [InlineData(@"Read from \\fileserver\share before continuing.")]
    public async Task Presentation_policy_rejects_additional_endpoint_forms(string answer)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new McpResponseComposer().ComposeAsync(
            Context(),
            new ModelResponse(answer, "test-model", false),
            []));
    }

    [Fact]
    public async Task Presentation_policy_rejects_exact_authenticated_grant_values()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new McpResponseComposer().ComposeAsync(
            Context(),
            new ModelResponse("This session permits brain.read.", "test-model", false),
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => new McpResponseComposer().ComposeAsync(
            context,
            new ModelResponse($"Authenticated {label}: {value}.", "test-model", false),
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => new McpResponseComposer().ComposeAsync(
            context,
            new ModelResponse(answer, "test-model", false),
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

        var composed = await new McpResponseComposer().ComposeAsync(
            context,
            new ModelResponse(answer, "test-model", false),
            []);

        Assert.Equal(answer, composed);
    }

    [Fact]
    public async Task Context_excludes_provider_results_but_preserves_safe_prompts_and_ordinary_history()
    {
        var context = Context();
        var store = new InoEffectStore();

        Complete("ordinary", "Explain the release plan.", "The release is planned for Tuesday.");
        Complete(
            "local-tool",
            "Show my workspace reminders.",
            "You have one workspace reminder.",
            grounding: Grounding("reminders.read.items"));
        Complete(
            "gmail",
            "Show my last incoming email.",
            "Private Gmail presentation text.",
            grounding: Grounding("gmail.read.messages"));
        Complete(
            "salesforce",
            "Find the matching Salesforce account.",
            "Private Salesforce presentation text.",
            groundings: [Grounding("salesforce.read.records")]);
        Complete(
            "cross-provider",
            "Match the latest sender to an account.",
            "Private cross-provider presentation text.",
            groundings: [Grounding("cross.match.salesforce-account-to-gmail-sender")]);

        const string currentPrompt = "What should I do next?";
        store.Begin(context, "current", currentPrompt);

        var assembled = await new McpConversationContextAssembler(store).AssembleAsync(
            new ConversationRequest(
                context,
                InoConversationIdentity.From(context),
                currentPrompt));

        Assert.Equal([
            "user: Explain the release plan.",
            "assistant: The release is planned for Tuesday.",
            "user: Show my workspace reminders.",
            "assistant: You have one workspace reminder.",
            "user: Show my last incoming email.",
            "user: Find the matching Salesforce account.",
            "user: Match the latest sender to an account."
        ], assembled.MemoryEvidence);

        void Complete(
            string commandId,
            string prompt,
            string response,
            ToolGrounding? grounding = null,
            IReadOnlyList<ToolGrounding>? groundings = null)
        {
            store.Begin(context, commandId, prompt);
            store.Transition(context, commandId, InoConversationStates.Running);
            store.Transition(context, commandId, InoConversationStates.Responding);
            store.Complete(context, commandId, response, grounding: grounding, groundings: groundings);
        }

        static ToolGrounding Grounding(string toolId) =>
            new(toolId, JsonSerializer.SerializeToElement(new { resultCount = 1 }));
    }

    [Fact]
    public async Task Long_unicode_answers_are_fitted_and_old_completed_turns_are_pruned_before_publication()
    {
        var context = Context();
        var feed = new PrivateFeedStore();
        var store = new InoEffectStore();
        var surfaces = new WorkspaceSurfaceProducer(feed, new ActionExecutor(feed), store);
        var longAnswer = string.Concat(Enumerable.Repeat("🧠", 20_000));
        var router = new RecordingModelRouter(_ => Task.FromResult(
            new ModelResponse(longAnswer, "test-model", false)));
        var handler = Handler(store, surfaces, router);

        for (var index = 0; index < 2; index++)
        {
            var result = await handler.ExecuteAsync(new CommandEnvelope(
                McpInoCommandHandler.CommandType, 2, $"long-answer-{index}", context,
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
                Assert.Equal(InoConversationStates.Succeeded, user.State);
            },
            assistant =>
            {
                Assert.Equal("long-answer-1", assistant.CommandId);
                Assert.Equal("assistant", assistant.Role);
                Assert.Equal(InoConversationStates.Succeeded, assistant.State);
                Assert.True(assistant.Text.EndsWith('…'));
                Assert.DoesNotContain("�", assistant.Text, StringComparison.Ordinal);
            });

        var payload = WorkspaceSurfaceProducer.BuildInoPayload(snapshot);
        var payloadBytes = Encoding.UTF8.GetByteCount(payload.GetRawText());
        Assert.InRange(payloadBytes, 1, WorkspaceSurfaceProducer.InoPayloadBudgetBytes);
        Assert.True(payloadBytes < PrivateFeedStore.MaximumSurfacePayloadBytes);
        var terminal = feed.CatchUp(context, SurfaceAudienceKind.Principal, 0).Items[^1];
        Assert.Equal(payload.GetRawText(), terminal.Payload.GetRawText());
    }

    [Fact]
    public void Durable_conversation_journal_requires_a_stable_key_and_rejects_tampering()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-ino-integrity-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "conversation.jsonl");
            var missingKey = Assert.Throws<ArgumentException>(() => new InoEffectStore(path));
            Assert.Equal("journalIntegrityKey", missingKey.ParamName);

            var journalKey = RandomNumberGenerator.GetBytes(32);
            var store = new InoEffectStore(path, journalIntegrityKey: journalKey);
            store.Begin(Context(), "tamper-command", Prompt);
            var lines = File.ReadAllLines(path);
            Assert.Single(lines);
            Assert.Contains(Prompt, lines[0], StringComparison.Ordinal);
            lines[0] = lines[0].Replace(Prompt, "Tampered prompt", StringComparison.Ordinal);
            File.WriteAllLines(path, lines);

            Assert.Throws<InvalidDataException>(() =>
                new InoEffectStore(path, journalIntegrityKey: journalKey));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
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
            var journalKey = RandomNumberGenerator.GetBytes(32);
            var firstStore = new InoEffectStore(conversationPath, journalIntegrityKey: journalKey);
            var firstFeed = new PrivateFeedStore(feedPath);
            var firstSurfaces = new WorkspaceSurfaceProducer(firstFeed, new ActionExecutor(firstFeed), firstStore);
            var firstRouter = new RecordingModelRouter(request => Task.FromResult(
                new ModelResponse("A durable answer to: " + request.Text, "test-model", false)));
            var command = new CommandEnvelope(McpInoCommandHandler.CommandType, 2, "stable-command", context,
                JsonSerializer.SerializeToElement(new { prompt = Prompt }));

            Assert.Equal(WorkflowState.Succeeded,
                (await Handler(firstStore, firstSurfaces, firstRouter).ExecuteAsync(command)).State);

            var journal = new AuthenticatedJsonLinesJournal(InoJournalDomain, journalKey, conversationPath)
                .Read()
                .Select(static record => JsonDocument.Parse(record.Payload))
                .ToArray();
            try
            {
                Assert.Equal(4, journal.Length);
                Assert.True(File.Exists(conversationPath + ".head"));
                var respondingPersisted = journal[^2].RootElement.GetProperty("Snapshot");
                Assert.Equal(InoConversationStates.Responding,
                    respondingPersisted.GetProperty("Operations")[0].GetProperty("State").GetString());
                Assert.DoesNotContain(respondingPersisted.GetProperty("Turns").EnumerateArray(), turn =>
                    turn.GetProperty("Role").GetString() == "assistant");
                var completed = journal[^1].RootElement.GetProperty("Snapshot");
                Assert.Equal(InoConversationStates.Succeeded,
                    completed.GetProperty("Operations")[0].GetProperty("State").GetString());
                Assert.Contains(completed.GetProperty("Turns").EnumerateArray(), turn =>
                    turn.GetProperty("Role").GetString() == "assistant" &&
                    turn.GetProperty("State").GetString() == InoConversationStates.Succeeded);
            }
            finally
            {
                foreach (var document in journal) document.Dispose();
            }

            var reopenedStore = new InoEffectStore(conversationPath, journalIntegrityKey: journalKey);
            var reopenedFeed = new PrivateFeedStore(feedPath);
            var reopenedSurfaces = new WorkspaceSurfaceProducer(
                reopenedFeed, new ActionExecutor(reopenedFeed), reopenedStore);
            var replayRouter = new RecordingModelRouter(_ => throw new InvalidOperationException("Replay called the model."));
            var restored = reopenedSurfaces.EnsureInitial(context, SurfaceAudienceKind.Principal);

            Assert.Equal(2, MessageCount(restored));
            Assert.Equal(WorkflowState.Succeeded,
                (await Handler(reopenedStore, reopenedSurfaces, replayRouter).ExecuteAsync(command)).State);
            Assert.Equal(0, replayRouter.Calls);
            Assert.Equal(2, reopenedStore.Read(context).Turns.Count);
            Assert.Equal(2, MessageCount(reopenedFeed.CatchUp(
                context, SurfaceAudienceKind.Principal, long.MaxValue).Items.Single()));
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
            var journalKey = RandomNumberGenerator.GetBytes(32);
            var store = new InoEffectStore(path, journalIntegrityKey: journalKey);
            const string commandId = "atomic-command";
            store.Begin(context, commandId, Prompt);
            store.Transition(context, commandId, InoConversationStates.Running);
            store.Transition(context, commandId, InoConversationStates.Responding);
            var recordsBeforeComplete = File.ReadAllLines(path).Length;
            var action = new ToolAction(
                "openUrl",
                "Connect Google",
                "https://accounts.google.com/o/oauth2/v2/auth?state=current");
            ToolGrounding[] groundings =
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
            Assert.Equal(InoConversationStates.Succeeded, operation.State);
            Assert.Equal(action, operation.Action);
            Assert.Equal(groundings[0].Content.GetRawText(), operation.Grounding!.Content.GetRawText());
            Assert.Equal(2, operation.Groundings!.Count);
            Assert.Equal("Atomic grounded answer.", Assert.Single(
                completed.Turns, static turn => turn.Role == "assistant").Text);

            var reopened = new InoEffectStore(path, journalIntegrityKey: journalKey);
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
    public void Reopening_neutralizes_obsolete_connection_actions_append_only_and_exactly_once()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-ino-action-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var context = Context();
            var path = Path.Combine(root, "conversation.jsonl");
            var journalKey = RandomNumberGenerator.GetBytes(32);
            const string commandId = "legacy-connection-action";
            var legacySnapshot = new InoConversationSnapshot(
                InoConversationIdentity.From(context),
                7,
                [
                    new InoConversationTurn(commandId, "user", Prompt, InoConversationStates.Succeeded),
                    new InoConversationTurn(commandId, "assistant", "Connect Salesforce to continue.", InoConversationStates.Succeeded)
                ],
                [
                    new InoConversationOperation(
                        commandId,
                        Prompt,
                        InoConversationStates.Succeeded,
                        null,
                        false,
                        DateTimeOffset.UtcNow,
                        new ToolAction(
                            "openUrl",
                            "Connect Salesforce",
                            "https://login.salesforce.com/services/oauth2/authorize?state=legacy"))
                ]);
            var legacyLine = JsonSerializer.Serialize(new
            {
                Version = 2,
                Tenant = context.TenantId,
                Workspace = context.WorkspaceId,
                Principal = context.Principal,
                Snapshot = legacySnapshot
            });
            File.WriteAllText(path, legacyLine + Environment.NewLine);
            var policy = new ToolActionPolicy("https://brain.example/oauth/callback/salesforce");

            var recoveredStore = new InoEffectStore(path, policy, journalKey);
            var recovered = recoveredStore.Read(context);
            var recoveredLines = File.ReadAllLines(path);

            Assert.Equal(8, recovered.Revision);
            Assert.Null(Assert.Single(recovered.Operations).Action);
            Assert.Equal(3, recoveredLines.Length);
            Assert.Equal(legacyLine, recoveredLines[0]);
            Assert.DoesNotContain("login.salesforce.com", recoveredLines[1], StringComparison.Ordinal);
            Assert.DoesNotContain("login.salesforce.com", recoveredLines[2], StringComparison.Ordinal);
            Assert.True(File.Exists(path + ".head"));

            var reopenedStore = new InoEffectStore(path, policy, journalKey);

            Assert.Equal(recovered.Revision, reopenedStore.Read(context).Revision);
            Assert.Equal(recoveredLines, File.ReadAllLines(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Reopening_prunes_oversized_version_three_history_without_discarding_the_journal()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-ino-bounded-migration-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var context = Context();
            var path = Path.Combine(root, "conversation.jsonl");
            var journalKey = RandomNumberGenerator.GetBytes(32);
            var turns = new List<InoConversationTurn>();
            var operations = new List<InoConversationOperation>();
            for (var index = 0; index < 80; index++)
            {
                var commandId = "completed-" + index;
                turns.Add(new InoConversationTurn(
                    commandId,
                    "user",
                    "Request " + index,
                    InoConversationStates.Succeeded));
                turns.Add(new InoConversationTurn(
                    commandId,
                    "assistant",
                    new string((char)('a' + index % 26), 1024),
                    InoConversationStates.Succeeded));
                operations.Add(new InoConversationOperation(
                    commandId,
                    "Request " + index,
                    InoConversationStates.Succeeded,
                    null,
                    false,
                    DateTimeOffset.UtcNow,
                    index == 0
                        ? new ToolAction(
                            "openUrl",
                            "Connect account",
                            "https://accounts.google.com/o/oauth2/v2/auth?state=historical")
                        : null));
            }
            var legacySnapshot = new InoConversationSnapshot(
                InoConversationIdentity.From(context),
                80,
                turns,
                operations);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Version = 3,
                Tenant = context.TenantId,
                Workspace = context.WorkspaceId,
                Principal = context.Principal,
                Snapshot = legacySnapshot
            }) + Environment.NewLine);

            var recovered = new InoEffectStore(path, journalIntegrityKey: journalKey).Read(context);
            var recoveredLines = File.ReadAllLines(path);

            Assert.True(recovered.Operations.Count < operations.Count);
            Assert.All(recovered.Operations, operation => Assert.Null(operation.Action));
            Assert.Equal(81, recovered.Revision);
            Assert.Equal(3, recoveredLines.Length);
            var reopened = new InoEffectStore(path, journalIntegrityKey: journalKey).Read(context);
            Assert.Equal(recovered.Revision, reopened.Revision);
            Assert.Equal(
                recovered.Operations.Select(static operation => operation.CommandId),
                reopened.Operations.Select(static operation => operation.CommandId));
            Assert.Equal(recoveredLines, File.ReadAllLines(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("https://accounts.google.com/o/oauth2/v2/auth?state=current", null)]
    [InlineData(
        "https://brain.example/oauth/start/salesforce?t=opaque-token",
        "https://brain.example/oauth/callback/salesforce")]
    public void Current_connection_actions_are_accepted(string target, string? salesforceRedirectUri)
    {
        var policy = new ToolActionPolicy(salesforceRedirectUri);

        Assert.True(policy.IsAllowed(new ToolAction("openUrl", "Connect account", target)));
    }

    [Theory]
    [InlineData("https://accounts.google.com/")]
    [InlineData("https://accounts.google.com:444/o/oauth2/v2/auth?state=current")]
    [InlineData("https://user@accounts.google.com/o/oauth2/v2/auth?state=current")]
    [InlineData("https://accounts.google.com/o/oauth2/v2/auth?state=current#fragment")]
    public void Google_connection_actions_require_the_exact_authorization_endpoint(string target)
    {
        var policy = new ToolActionPolicy();

        Assert.False(policy.IsAllowed(new ToolAction("openUrl", "Connect account", target)));
    }

    [Fact]
    public async Task Interrupted_legacy_partial_answer_recovers_failed_and_replay_never_claims_success()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-ino-interrupted-answer-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = Context();
            var path = Path.Combine(root, "conversation.jsonl");
            var journalKey = RandomNumberGenerator.GetBytes(32);
            var store = new InoEffectStore();
            const string commandId = "interrupted-command";
            store.Begin(context, commandId, Prompt);
            store.Transition(context, commandId, InoConversationStates.Running);
            store.Transition(context, commandId, InoConversationStates.Responding);
            var responding = store.Read(context);
            var legacyPartial = responding with
            {
                Revision = checked(responding.Revision + 1),
                Turns = responding.Turns.Concat([
                    new InoConversationTurn(
                        commandId,
                        "assistant",
                        "This answer was never atomically committed.",
                        InoConversationStates.Responding)
                ]).ToArray()
            };
            Directory.CreateDirectory(root);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Version = 3,
                Tenant = context.TenantId,
                Workspace = context.WorkspaceId,
                Principal = context.Principal,
                Snapshot = legacyPartial
            }) + Environment.NewLine);

            var recoveredStore = new InoEffectStore(path, journalIntegrityKey: journalKey);
            var recovered = recoveredStore.Read(context);
            var recoveredOperation = Assert.Single(recovered.Operations);
            Assert.Equal(InoConversationStates.Failed, recoveredOperation.State);
            Assert.Contains("couldn’t confirm", recoveredOperation.SafeReason, StringComparison.Ordinal);
            Assert.Null(recoveredOperation.Action);
            Assert.Null(recoveredOperation.Grounding);
            Assert.Null(recoveredOperation.Groundings);
            var recoveredTurn = Assert.Single(recovered.Turns);
            Assert.Equal("user", recoveredTurn.Role);
            Assert.Equal(InoConversationStates.Failed, recoveredTurn.State);

            var feed = new PrivateFeedStore();
            var surfaces = new WorkspaceSurfaceProducer(feed, new ActionExecutor(feed), recoveredStore);
            var replayRouter = new RecordingModelRouter(_ => throw new InvalidOperationException("Replay called the model."));
            var command = new CommandEnvelope(
                McpInoCommandHandler.CommandType,
                2,
                commandId,
                context,
                JsonSerializer.SerializeToElement(new { prompt = Prompt }));

            var replay = await Handler(recoveredStore, surfaces, replayRouter).ExecuteAsync(command);

            Assert.Equal(WorkflowState.Failed, replay.State);
            Assert.Equal(0, replayRouter.Calls);
            Assert.Equal(InoConversationStates.Failed, recoveredStore.Read(context).CurrentOperation!.State);
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
        var feed = new PrivateFeedStore();
        var actions = new ActionExecutor(feed);
        var surface = new WorkspaceSurfaceProducer(feed, actions).EnsureInitial(
            context, SurfaceAudienceKind.Principal);
        var binding = Assert.Single(surface.Actions);
        Assert.Equal(WorkspaceSurfaceProducer.InoBindingId, binding.BindingId);
        Assert.Equal(WorkspaceSurfaceProducer.InoActionType, binding.ActionType);
        var issued = actions.Issue(context, surface, binding, TimeSpan.FromMinutes(1));
        var application = new ApplicationService();
        var input = JsonSerializer.SerializeToElement(new { prompt = Prompt });

        var firstAuthorization = await actions.ReserveAsync(context, issued.BindingId, issued.Token,
            surface.SurfaceId, surface.Revision, input, CancellationToken.None);
        var firstContext = context with
        {
            IdempotencyKey = firstAuthorization.Submission.IdempotencyKey,
            Grants = context.Grants.Append("brain.act").ToHashSet(StringComparer.Ordinal)
        };
        var first = await application.SubmitAsync(firstContext, new CommandEnvelope(
            firstAuthorization.Submission.ActionType, 2, "first-command", firstContext, input));
        Assert.True(actions.Commit(firstAuthorization, first.OperationId));

        var replayAuthorization = await actions.ReserveAsync(context, issued.BindingId, issued.Token,
            surface.SurfaceId, surface.Revision, input, CancellationToken.None);
        var replayContext = firstContext with { IdempotencyKey = replayAuthorization.Submission.IdempotencyKey };
        var replay = await application.SubmitAsync(replayContext, new CommandEnvelope(
            replayAuthorization.Submission.ActionType, 2, "replay-command", replayContext, input));
        Assert.True(actions.Commit(replayAuthorization, replay.OperationId));
        Assert.Equal(first.OperationId, replay.OperationId);

        var changedInput = JsonSerializer.SerializeToElement(new { prompt = "A changed prompt" });
        var changedAuthorization = await actions.ReserveAsync(context, issued.BindingId, issued.Token,
            surface.SurfaceId, surface.Revision, changedInput, CancellationToken.None);
        var changedContext = firstContext with { IdempotencyKey = changedAuthorization.Submission.IdempotencyKey };
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => application.SubmitAsync(
            changedContext, new CommandEnvelope(changedAuthorization.Submission.ActionType, 2,
                "changed-command", changedContext, changedInput)));
        actions.Release(changedAuthorization);
    }

    [Fact]
    public async Task Authorization_wait_is_durable_attempt_idempotent_and_resumes_exact_typed_invocation_once()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-ino-authorization-resume-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var context = Context() with { IdempotencyKey = "authorization-resume" };
            var operationsPath = Path.Combine(root, "operations.jsonl");
            var conversationPath = Path.Combine(root, "conversation.jsonl");
            var journalKey = RandomNumberGenerator.GetBytes(32);
            var invocationInput = JsonSerializer.SerializeToElement(new
            {
                provider = "gmail",
                operation = "list",
                limit = 2,
                filters = new[] { new { field = "direction", value = "incoming" } }
            });
            var invocation = new ToolInvocation("gmail.read.messages", invocationInput);
            var planner = new FixedInvocationPlanner(invocation);
            var catalog = new AuthorizationSequenceCatalog(invocation);
            var firstStore = new InoEffectStore(conversationPath, journalIntegrityKey: journalKey);
            var firstFeed = new PrivateFeedStore();
            var firstHandler = AuthorizationHandler(firstStore, firstFeed, planner, catalog);
            var firstApplication = new ApplicationService(
                storagePath: operationsPath,
                journalIntegrityKey: journalKey);
            var command = new CommandEnvelope(
                McpInoCommandHandler.CommandType,
                2,
                "authorization-resume-command",
                context,
                JsonSerializer.SerializeToElement(new { prompt = "Get my last 2 incoming emails" }));

            var submitted = await firstApplication.SubmitAsync(context, command);
            Assert.True(firstApplication.TryClaimPending(
                submitted.OperationId,
                out var claimedCommand,
                out var claimedAuthorization));
            Assert.Null(claimedAuthorization);
            var interruptedResult = await firstHandler.ExecuteAsync(
                new CommandExecutionAttempt(claimedCommand!, claimedAuthorization));
            Assert.Equal(WorkflowState.AwaitingExternalAuthorization, interruptedResult.State);
            var interruptedConversation = firstStore.Read(context);
            Assert.Equal(InoConversationStates.AwaitingAuthorization,
                interruptedConversation.CurrentOperation!.State);
            Assert.True(interruptedConversation.CurrentOperation.Authorization!.Matches(
                interruptedResult.Authorization!));
            Assert.Equal(1, catalog.Calls);
            Assert.Equal(1, planner.Calls);

            // Simulate a crash after the INO snapshot committed but before the application journal
            // recorded the continuation. Recovery must repair the second journal without replanning.
            var reopenedApplication = new ApplicationService(
                storagePath: operationsPath,
                journalIntegrityKey: journalKey);
            Assert.Empty(reopenedApplication.GetAwaitingExternalAuthorizations());
            var reopenedStore = new InoEffectStore(conversationPath, journalIntegrityKey: journalKey);
            var resumedFeed = new PrivateFeedStore();
            var resumedHandler = AuthorizationHandler(reopenedStore, resumedFeed, planner, catalog);
            var resumedDispatcher = new CommandDispatcher(reopenedApplication, [resumedHandler]);

            Assert.True(await resumedDispatcher.DispatchAsync(submitted.OperationId));

            var durableWait = Assert.Single(reopenedApplication.GetAwaitingExternalAuthorizations());
            Assert.Equal(submitted.OperationId, durableWait.OperationId);
            Assert.Equal("google", durableWait.Continuation.Provider);
            Assert.Equal(invocation.ToolId, durableWait.Continuation.Invocation.ToolId);
            Assert.Equal(invocation.Input.GetRawText(), durableWait.Continuation.Invocation.Input.GetRawText());
            Assert.True(Guid.TryParseExact(durableWait.Continuation.AttemptId, "N", out _));

            Assert.False(reopenedApplication.TryRequeueExternalAuthorization(
                submitted.OperationId,
                Guid.NewGuid().ToString("N")));
            Assert.True(reopenedApplication.TryRequeueExternalAuthorization(
                submitted.OperationId,
                durableWait.Continuation.AttemptId));
            Assert.False(reopenedApplication.TryRequeueExternalAuthorization(
                submitted.OperationId,
                durableWait.Continuation.AttemptId));

            var callsBeforeResume = catalog.Calls;

            Assert.True(await resumedDispatcher.DispatchAsync(submitted.OperationId));

            var completed = await reopenedApplication.GetOperationAsync(context, submitted.OperationId);
            Assert.Equal(WorkflowState.Succeeded, completed!.State);
            Assert.Empty(reopenedApplication.GetAwaitingExternalAuthorizations());
            Assert.Equal(callsBeforeResume + 1, catalog.Calls);
            Assert.Equal(2, catalog.Calls);
            Assert.Equal(1, planner.Calls);
            Assert.All(catalog.Invocations, actual =>
            {
                Assert.Equal(invocation.ToolId, actual.ToolId);
                Assert.Equal(invocation.Input.GetRawText(), actual.Input.GetRawText());
            });
            var conversation = reopenedStore.Read(context);
            Assert.Equal(InoConversationStates.Succeeded, conversation.CurrentOperation!.State);
            Assert.Null(conversation.CurrentOperation.Action);
            Assert.Equal("Here are your last 2 incoming emails.",
                Assert.Single(conversation.Turns, static turn => turn.Role == "assistant").Text);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Failed_authorization_is_journaled_without_invoking_the_provider_again()
    {
        var context = Context() with { IdempotencyKey = "authorization-failed" };
        var invocation = new ToolInvocation(
            "gmail.read.messages",
            JsonSerializer.SerializeToElement(new { limit = 2 }));
        var planner = new FixedInvocationPlanner(invocation);
        var catalog = new AuthorizationSequenceCatalog(invocation);
        var store = new InoEffectStore();
        var handler = AuthorizationHandler(store, new PrivateFeedStore(), planner, catalog);
        var application = new ApplicationService();
        var dispatcher = new CommandDispatcher(application, [handler]);
        var command = new CommandEnvelope(
            McpInoCommandHandler.CommandType,
            2,
            "authorization-failed-command",
            context,
            JsonSerializer.SerializeToElement(new { prompt = "Get my last 2 incoming emails" }));
        var submitted = await application.SubmitAsync(context, command);

        Assert.True(await dispatcher.DispatchAsync(submitted.OperationId));
        var wait = Assert.Single(application.GetAwaitingExternalAuthorizations());
        Assert.True(application.TryRequeueExternalAuthorization(
            submitted.OperationId,
            wait.Continuation.AttemptId,
            new ExternalAuthorizationResolution(
                ExternalAuthorizationResolutionState.Failed,
                "authorization-failed")));

        Assert.True(await dispatcher.DispatchAsync(submitted.OperationId));

        var completed = await application.GetOperationAsync(context, submitted.OperationId);
        Assert.Equal(WorkflowState.Failed, completed!.State);
        Assert.Equal("authorization-failed", completed.SafeReason);
        Assert.Equal(1, catalog.Calls);
        Assert.Equal(1, planner.Calls);
        Assert.Equal(InoConversationStates.Failed, store.Read(context).CurrentOperation!.State);
    }

    private static McpInoCommandHandler Handler(
        IInoConversationStore store,
        WorkspaceSurfaceProducer surfaces,
        IModelRouter router) =>
        new(store, surfaces, new ConversationOwner(
            new McpConversationContextAssembler(store),
            new McpNoToolPlanner(),
            router,
            new McpNoToolCatalog(),
            new McpResponseComposer()));

    private static McpInoCommandHandler AuthorizationHandler(
        IInoConversationStore store,
        PrivateFeedStore feed,
        IIntentCapabilityPlanner planner,
        IAuthorizedToolCatalog catalog) =>
        new(
            store,
            new WorkspaceSurfaceProducer(feed, new ActionExecutor(feed), store),
            new ConversationOwner(
                new McpConversationContextAssembler(store),
                planner,
                new RecordingModelRouter(_ => throw new InvalidOperationException("Authorization flow called the model.")),
                catalog,
                new AuthorizationResponseComposer()));

    private static RuntimeRequestContext Context() => new(
        new TenantId("private-tenant"),
        new WorkspaceId("private-workspace"),
        new PrincipalRef("private-user", PrincipalKind.User),
        "session",
        AuthAssurance.Password,
        "correlation",
        "exact-idempotency",
        new HashSet<string> { "brain.read", "brain.act", "ui.action" });

    private static string OperationState(StoredSurfaceRecord record) =>
        record.Payload.GetProperty("data").GetProperty("operation").GetProperty("state").GetString()!;

    private static int MessageCount(StoredSurfaceRecord record) =>
        record.Payload.GetProperty("data").GetProperty("messages").GetArrayLength();

    private sealed class FixedInvocationPlanner(ToolInvocation invocation) : IIntentCapabilityPlanner
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ToolInvocation>> PlanAsync(
            ConversationRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<ToolInvocation>>([
                new ToolInvocation(invocation.ToolId, invocation.Input.Clone())
            ]);
        }
    }

    private sealed class AuthorizationSequenceCatalog(ToolInvocation expected) : IAuthorizedToolCatalog
    {
        private readonly List<ToolInvocation> _invocations = [];
        public int Calls => _invocations.Count;
        public IReadOnlyList<ToolInvocation> Invocations => _invocations;

        public Task<ToolOutcome> InvokeAsync(
            RuntimeRequestContext context,
            ToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(expected.ToolId, invocation.ToolId);
            Assert.Equal(expected.Input.GetRawText(), invocation.Input.GetRawText());
            _invocations.Add(new ToolInvocation(invocation.ToolId, invocation.Input.Clone()));
            if (_invocations.Count == 1)
            {
                return Task.FromResult(new ToolOutcome(
                    ToolOutcomeKind.NeedsAuth,
                    SafeReason: "Google authorization is required.",
                    Action: new ToolAction(
                        "openUrl",
                        "Connect Google",
                        "https://accounts.google.com/o/oauth2/v2/auth?state=current"),
                    AuthorizationProvider: "google"));
            }

            if (_invocations.Count == 2)
            {
                var content = JsonSerializer.SerializeToElement(new
                {
                    resultCount = 2,
                    messageIds = new[] { "message-1", "message-2" }
                });
                return Task.FromResult(new ToolOutcome(
                    ToolOutcomeKind.Success,
                    Content: content,
                    GroundingContent: content));
            }

            throw new InvalidOperationException("The stored tool invocation was replayed more than once.");
        }
    }

    private sealed class AuthorizationResponseComposer : IResponseSurfaceComposer
    {
        public Task<string> ComposeAsync(
            RuntimeRequestContext context,
            ModelResponse response,
            IReadOnlyList<ToolOutcome> toolOutcomes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Assert.Single(toolOutcomes).Kind == ToolOutcomeKind.NeedsAuth
                ? "Connect your Google account to continue."
                : "Here are your last 2 incoming emails.");
    }

    private sealed class RecordingModelRouter(
        Func<ModelRequest, Task<ModelResponse>> complete) : IModelRouter
    {
        public int Calls { get; private set; }

        public Task<ModelResponse> CompleteAsync(
            ModelRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return complete(request);
        }
    }
}
