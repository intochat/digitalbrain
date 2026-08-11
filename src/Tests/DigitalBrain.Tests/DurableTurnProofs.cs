using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Execution;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

// S1.5 GREEN: durable conversation turns on the Execution kernel (P0-2, P0-6 flipped).
public sealed class DurableTurnCompositionProofs
{
    [Fact]
    public void MapOwnerCommandsDetachesRequestAbortFromTheAiRun()
    {
        var source = ReadRepoFile("src", "Kernel", "DigitalBrain.Kernel", "MapOwnerCommands.cs");
        Assert.DoesNotContain("CreateLinkedTokenSource(requestAborted)", source, StringComparison.Ordinal);
        Assert.Contains(".Send(new SendMessage", source, StringComparison.Ordinal);
        Assert.Contains("WatchJournalAsync", source, StringComparison.Ordinal);
        Assert.Contains("requestAborted only detaches", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSendStartsAnExecutionInsteadOfBindingTheCallerTokenToTheResponder()
    {
        var source = ReadRepoFile(
            "src", "Modules", "UI", "DigitalBrain.Modules.UI", "Chat", "Chat.cs");
        Assert.Contains("StartExecution", source, StringComparison.Ordinal);
        Assert.Contains("IExecution", source, StringComparison.Ordinal);
        Assert.Contains("RequireActor", source, StringComparison.Ordinal);
        Assert.Contains("chat-turn-", source, StringComparison.Ordinal);
        Assert.Contains("Origin:", source, StringComparison.Ordinal);
        Assert.Contains("ExecutionTerminal", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CompleteTurnWork", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatTurnWorkerUsesDirectedDispatchWithoutBroadcastIHandle()
    {
        var source = ReadRepoFile(
            "src", "Modules", "UI", "DigitalBrain.Modules.UI", "Chat", "ChatTurnWorker.cs");
        Assert.Contains("OnUnboundSynapseAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IHandle<DispatchWorkerAccept>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IHandle<DispatchWorkerCancel>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CompleteTurnWork", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerDispatchRelayValidatesAllowListedWorkerTypes()
    {
        var source = ReadRepoFile(
            "src", "Modules", "Execution", "Execution", "WorkerDispatchRelayNeuron.cs");
        Assert.Contains("WorkerGrainTypeRegistry", source, StringComparison.Ordinal);
        Assert.Contains("worker-type-not-registered", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectAgentSessionPersistsAtToolRoundSafePoints()
    {
        var source = ReadRepoFile(
            "src", "Modules", "AI", "AI", "Orchestration", "DirectAgentSession.cs");
        var runStreaming = ExtractMethodBody(source, "RunStreamingAsync");
        Assert.Contains("await foreach", runStreaming, StringComparison.Ordinal);
        Assert.Contains("IsToolRoundSafePoint", runStreaming, StringComparison.Ordinal);

        var loopStart = runStreaming.IndexOf("await foreach", StringComparison.Ordinal);
        var loopOpen = runStreaming.IndexOf('{', loopStart);
        Assert.True(loopOpen > 0);
        var depth = 0;
        var loopEnd = loopOpen;
        for (var i = loopOpen; i < runStreaming.Length; i++)
        {
            if (runStreaming[i] == '{')
            {
                depth++;
            }
            else if (runStreaming[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    loopEnd = i;
                    break;
                }
            }
        }

        var foreachBody = runStreaming[loopOpen..(loopEnd + 1)];
        Assert.Contains("PersistSessionAsync", foreachBody, StringComparison.Ordinal);

        var afterLoop = runStreaming[(loopEnd + 1)..];
        Assert.Contains("PersistSessionAsync", afterLoop, StringComparison.Ordinal);
        Assert.Contains("FunctionResultContent", source, StringComparison.Ordinal);
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        var signature = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        Assert.True(signature >= 0, $"Method '{methodName}' not found.");
        var bodyOpen = source.IndexOf('{', signature);
        Assert.True(bodyOpen > 0);
        var depth = 0;
        for (var i = bodyOpen; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[signature..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Could not extract body for '{methodName}'.");
    }

    private static string ReadRepoFile(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate {string.Join('/', relativeParts)} from the test base directory.");
    }
}

[Collection(BrainCollection.Name)]
public sealed class DurableTurnBehaviorProofs(BrainClusterFixture fixture)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task SendReturnsTurnIdAndCompletesThroughExecutionIndependentlyOfObserverAbort()
    {
        var brain = fixture.BrainFor("p02-abort-detaches");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var agent = new NeuronId("scriptedagent", brain.Owner, "hold-detach");
        var actor = TestActors.Operator;

        ScriptedAgent.ConfigureHold("hold-detach");
        try
        {
            await brain.FireAsync<ISynapseGraph>(
                ISynapseGraph.InstanceName,
                new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, agent),
                TestContext.Current.CancellationToken);
            await Graphs.WaitForConnectionTargetAsync(brain, chat, ChatRoles.Responder, agent);

            using var abort = new CancellationTokenSource();
            var command = CommandId.New();
            var stream = brain.GetGrainProxy<IChat>("main")
                .SendStreaming(new SendMessage(command, "will detach", actor), abort.Token);

            await using var enumerator = stream.GetAsyncEnumerator(abort.Token);
            // SendStreaming enqueues and returns (observer stream is empty by design).
            Assert.False(await enumerator.MoveNextAsync());

            await Journals.WaitForAsync(
                brain, chat, JournalKind.Outgoing,
                delivery => delivery.Synapse is UserMessaged { Text: "will detach" });

            var acceptedTurns = await brain.GetGrainProxy<IChat>("main").ReadTurns();
            var turn = Assert.Single(acceptedTurns, t => t.CommandId == command);
            Assert.True(
                turn.Status is ChatTurnStatus.Pending or ChatTurnStatus.Running,
                $"Expected pending/running, got {turn.Status}");

            await ScriptedAgent.WaitUntilHeldAsync("hold-detach", Patience);
            await abort.CancelAsync();

            // Release after observer abort — AI must still complete (P0-2 flipped).
            ScriptedAgent.ReleaseHold("hold-detach");

            await Journals.WaitForAsync(
                brain, chat, JournalKind.Outgoing,
                delivery => delivery.Synapse is Responded { Text: "scripted:hold-detach" });

            var finalTurns = await brain.GetGrainProxy<IChat>("main").ReadTurns();
            var completed = Assert.Single(finalTurns, t => t.TurnId == turn.TurnId);
            Assert.Equal(ChatTurnStatus.Completed, completed.Status);
        }
        finally
        {
            ScriptedAgent.ReleaseHold("hold-detach");
            ScriptedAgent.ClearHold("hold-detach");
        }
    }

    [Fact]
    public async Task FifoQueueRunsTurnsInArrivalOrderWithinOneConversation()
    {
        var brain = fixture.BrainFor("fifo-queue");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var agent = new NeuronId("scriptedagent", brain.Owner, "fifo-agent");
        var actor = TestActors.Operator;

        ScriptedAgent.ConfigureHold("fifo-agent");
        try
        {
            await brain.FireAsync<ISynapseGraph>(
                ISynapseGraph.InstanceName,
                new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, agent),
                TestContext.Current.CancellationToken);
            await Graphs.WaitForConnectionTargetAsync(brain, chat, ChatRoles.Responder, agent);

            var first = await brain.GetGrainProxy<IChat>("main")
                .Send(new SendMessage(CommandId.New(), "first", actor));
            var second = await brain.GetGrainProxy<IChat>("main")
                .Send(new SendMessage(CommandId.New(), "second", actor));

            await ScriptedAgent.WaitUntilHeldAsync("fifo-agent", Patience);
            Assert.Equal(1, ScriptedAgent.AcceptCount("fifo-agent"));

            var mid = await brain.GetGrainProxy<IChat>("main").ReadTurns();
            Assert.Equal(ChatTurnStatus.Running, mid.Single(t => t.TurnId == first.TurnId).Status);
            Assert.Equal(ChatTurnStatus.Pending, mid.Single(t => t.TurnId == second.TurnId).Status);

            // Release the head; the completed hold TCS lets the queued second Accept proceed immediately.
            ScriptedAgent.ReleaseHold("fifo-agent");

            await WaitForTurnStatusAsync(brain, "main", first.TurnId, ChatTurnStatus.Completed);
            await WaitForTurnStatusAsync(brain, "main", second.TurnId, ChatTurnStatus.Completed);

            Assert.True(ScriptedAgent.AcceptCount("fifo-agent") >= 2);
        }
        finally
        {
            ScriptedAgent.ReleaseHold("fifo-agent");
            ScriptedAgent.ClearHold("fifo-agent");
        }
    }

    [Fact]
    public async Task DifferentConversationsRunConcurrently()
    {
        var brain = fixture.BrainFor("concurrent-chats");
        var chatA = NeuronId.For<IChat>(brain.Owner, "a");
        var chatB = NeuronId.For<IChat>(brain.Owner, "b");
        var agentA = new NeuronId("scriptedagent", brain.Owner, "conc-a");
        var agentB = new NeuronId("scriptedagent", brain.Owner, "conc-b");
        var actor = TestActors.Operator;

        ScriptedAgent.ConfigureHold("conc-a");
        ScriptedAgent.ConfigureHold("conc-b");
        try
        {
            await brain.FireAsync<ISynapseGraph>(
                ISynapseGraph.InstanceName,
                new Connect(ChatRoles.ResponderConnectionId(chatA), chatA, ChatRoles.Responder, agentA),
                TestContext.Current.CancellationToken);
            await brain.FireAsync<ISynapseGraph>(
                ISynapseGraph.InstanceName,
                new Connect(ChatRoles.ResponderConnectionId(chatB), chatB, ChatRoles.Responder, agentB),
                TestContext.Current.CancellationToken);
            await Graphs.WaitForConnectionTargetAsync(brain, chatA, ChatRoles.Responder, agentA);
            await Graphs.WaitForConnectionTargetAsync(brain, chatB, ChatRoles.Responder, agentB);

            await brain.GetGrainProxy<IChat>("a").Send(new SendMessage(CommandId.New(), "from a", actor));
            await brain.GetGrainProxy<IChat>("b").Send(new SendMessage(CommandId.New(), "from b", actor));

            await ScriptedAgent.WaitUntilHeldAsync("conc-a", Patience);
            await ScriptedAgent.WaitUntilHeldAsync("conc-b", Patience);
            Assert.Equal(1, ScriptedAgent.AcceptCount("conc-a"));
            Assert.Equal(1, ScriptedAgent.AcceptCount("conc-b"));

            ScriptedAgent.ReleaseHold("conc-a");
            ScriptedAgent.ReleaseHold("conc-b");

            await Journals.WaitForAsync(
                brain, chatA, JournalKind.Outgoing,
                delivery => delivery.Synapse is Responded { Text: "scripted:conc-a" });
            await Journals.WaitForAsync(
                brain, chatB, JournalKind.Outgoing,
                delivery => delivery.Synapse is Responded { Text: "scripted:conc-b" });
        }
        finally
        {
            ScriptedAgent.ReleaseHold("conc-a");
            ScriptedAgent.ReleaseHold("conc-b");
            ScriptedAgent.ClearHold("conc-a");
            ScriptedAgent.ClearHold("conc-b");
        }
    }

    [Fact]
    public async Task CancelQueuedTurnAdvancesTheQueue()
    {
        var brain = fixture.BrainFor("cancel-queue");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var agent = new NeuronId("scriptedagent", brain.Owner, "cancel-agent");
        var actor = TestActors.Operator;

        ScriptedAgent.ConfigureHold("cancel-agent");
        try
        {
            await brain.FireAsync<ISynapseGraph>(
                ISynapseGraph.InstanceName,
                new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, agent),
                TestContext.Current.CancellationToken);
            await Graphs.WaitForConnectionTargetAsync(brain, chat, ChatRoles.Responder, agent);

            var first = await brain.GetGrainProxy<IChat>("main")
                .Send(new SendMessage(CommandId.New(), "running", actor));
            var second = await brain.GetGrainProxy<IChat>("main")
                .Send(new SendMessage(CommandId.New(), "queued", actor));

            await ScriptedAgent.WaitUntilHeldAsync("cancel-agent", Patience);

            await brain.GetGrainProxy<IChat>("main").Cancel(
                new CancelTurn(CommandId.New(), second.TurnId, actor));

            var afterCancel = await brain.GetGrainProxy<IChat>("main").ReadTurns();
            Assert.Equal(ChatTurnStatus.Cancelled, afterCancel.Single(t => t.TurnId == second.TurnId).Status);
            Assert.Equal(ChatTurnStatus.Running, afterCancel.Single(t => t.TurnId == first.TurnId).Status);

            ScriptedAgent.ReleaseHold("cancel-agent");
            await WaitForTurnStatusAsync(brain, "main", first.TurnId, ChatTurnStatus.Completed);
        }
        finally
        {
            ScriptedAgent.ReleaseHold("cancel-agent");
            ScriptedAgent.ClearHold("cancel-agent");
        }
    }

    [Fact]
    public async Task CancelRunningTurnIsVersionedAndIdempotent()
    {
        var brain = fixture.BrainFor("cancel-running");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var agent = new NeuronId("scriptedagent", brain.Owner, "cancel-run");
        var actor = TestActors.Operator;

        ScriptedAgent.ConfigureHold("cancel-run");
        try
        {
            await brain.FireAsync<ISynapseGraph>(
                ISynapseGraph.InstanceName,
                new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, agent),
                TestContext.Current.CancellationToken);
            await Graphs.WaitForConnectionTargetAsync(brain, chat, ChatRoles.Responder, agent);

            var accepted = await brain.GetGrainProxy<IChat>("main")
                .Send(new SendMessage(CommandId.New(), "cancel me", actor));
            var queued = await brain.GetGrainProxy<IChat>("main")
                .Send(new SendMessage(CommandId.New(), "wait behind cancel", actor));

            await ScriptedAgent.WaitUntilHeldAsync("cancel-run", Patience);
            var acceptsBeforeCancel = ScriptedAgent.AcceptCount("cancel-run");

            var cancelCommand = CommandId.New();
            await brain.GetGrainProxy<IChat>("main").Cancel(
                new CancelTurn(cancelCommand, accepted.TurnId, actor));
            // Idempotent: already Cancelling / terminal — second cancel is a no-op.
            await brain.GetGrainProxy<IChat>("main").Cancel(
                new CancelTurn(CommandId.New(), accepted.TurnId, actor));

            // Head stays Cancelling until the kernel terminal bridge advances the queue.
            await WaitForAsync(async () =>
            {
                var turns = await brain.GetGrainProxy<IChat>("main").ReadTurns();
                var head = turns.First(t => t.TurnId == accepted.TurnId);
                var next = turns.First(t => t.TurnId == queued.TurnId);
                return head.Status is ChatTurnStatus.Cancelling or ChatTurnStatus.Cancelled
                    && next.Status == ChatTurnStatus.Pending
                    && ScriptedAgent.AcceptCount("cancel-run") == acceptsBeforeCancel;
            });

            await WaitForTurnStatusAsync(brain, "main", accepted.TurnId, ChatTurnStatus.Cancelled);
            Assert.True(ScriptedAgent.WasCancelled("cancel-run"));

            // Release the agent hold so the next turn (which only starts after the head
            // is terminal) can finish its Accept.
            ScriptedAgent.ReleaseHold("cancel-run");
            await WaitForTurnStatusAsync(brain, "main", queued.TurnId, ChatTurnStatus.Completed);
            Assert.True(ScriptedAgent.AcceptCount("cancel-run") > acceptsBeforeCancel);

            var transcript = await brain.GetGrainProxy<IChat>("main").Read();
            // Cancelled head must not leave a reply; the queued turn may reply once after.
            var assistantReplies = transcript.Turns.Count(t => !t.FromUser && t.Text == "scripted:cancel-run");
            Assert.Equal(1, assistantReplies);
        }
        finally
        {
            ScriptedAgent.ReleaseHold("cancel-run");
            ScriptedAgent.ClearHold("cancel-run");
        }
    }

    [Fact]
    public async Task ActorlessSendIsRefusedAtTheGrain()
    {
        var brain = fixture.BrainFor("actor-refuse");
        await Assert.ThrowsAsync<NeuronAuthorizationException>(async () =>
            await brain.GetGrainProxy<IChat>("main")
                .Send(new SendMessage(CommandId.New(), "no actor")));
    }

    [Fact]
    public async Task RunningTurnSurvivesSiloRestartAndCompletes()
    {
        var brain = fixture.BrainFor("turn-restart");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var agent = new NeuronId("scriptedagent", brain.Owner, "restart-agent");
        var actor = TestActors.Operator;

        ScriptedAgent.ConfigureHold("restart-agent");
        try
        {
            await brain.FireAsync<ISynapseGraph>(
                ISynapseGraph.InstanceName,
                new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, agent),
                TestContext.Current.CancellationToken);
            await Graphs.WaitForConnectionTargetAsync(brain, chat, ChatRoles.Responder, agent);

            var accepted = await brain.GetGrainProxy<IChat>("main")
                .Send(new SendMessage(CommandId.New(), "restart me", actor));
            var queued = await brain.GetGrainProxy<IChat>("main")
                .Send(new SendMessage(CommandId.New(), "after restart", actor));

            await ScriptedAgent.WaitUntilHeldAsync("restart-agent", Patience);
            var before = await brain.GetGrainProxy<IChat>("main").ReadTurns();
            var beforeTurn = Assert.Single(before, t => t.TurnId == accepted.TurnId);
            Assert.Equal(ChatTurnStatus.Running, beforeTurn.Status);
            Assert.False(string.IsNullOrWhiteSpace(beforeTurn.ExecutionName));

            await fixture.RestartSilosAsync();

            // After restart the head must reach a terminal status (not freeze Running forever).
            await WaitForAsync(async () =>
            {
                try
                {
                    var turns = await brain.GetGrainProxy<IChat>("main").ReadTurns();
                    var head = turns.FirstOrDefault(t => t.TurnId == accepted.TurnId);
                    return head is
                    {
                        Status: ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled
                    };
                }
                catch
                {
                    return false;
                }
            });

            var finalHead = (await brain.GetGrainProxy<IChat>("main").ReadTurns())
                .First(t => t.TurnId == accepted.TurnId);
            Assert.True(
                finalHead.Status is ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled,
                $"Expected terminal head after restart, got {finalHead.Status}");

            // Queue must advance past the recovered head.
            ScriptedAgent.ConfigureHold("restart-agent");
            ScriptedAgent.ReleaseHold("restart-agent");
            await WaitForAsync(async () =>
            {
                var turns = await brain.GetGrainProxy<IChat>("main").ReadTurns();
                var next = turns.FirstOrDefault(t => t.TurnId == queued.TurnId);
                return next is
                {
                    Status: ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled
                        or ChatTurnStatus.Running
                };
            });
        }
        finally
        {
            ScriptedAgent.ReleaseHold("restart-agent");
            ScriptedAgent.ClearHold("restart-agent");
        }
    }

    [Fact]
    public async Task KilledWorkerReachesFailedAndQueueAdvances()
    {
        var brain = fixture.BrainFor("killed-worker");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var agent = new NeuronId("scriptedagent", brain.Owner, "kill-agent");
        var actor = TestActors.Operator;

        ScriptedAgent.ConfigureHold("kill-agent");
        try
        {
            await brain.FireAsync<ISynapseGraph>(
                ISynapseGraph.InstanceName,
                new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, agent),
                TestContext.Current.CancellationToken);
            await Graphs.WaitForConnectionTargetAsync(brain, chat, ChatRoles.Responder, agent);

            var head = await brain.GetGrainProxy<IChat>("main")
                .Send(new SendMessage(CommandId.New(), "will die", actor));
            var next = await brain.GetGrainProxy<IChat>("main")
                .Send(new SendMessage(CommandId.New(), "should run after fail", actor));

            await ScriptedAgent.WaitUntilHeldAsync("kill-agent", Patience);
            // Silo restart kills the in-flight worker without a cooperative finish.
            await fixture.RestartSilosAsync();

            // Head must leave Running: restart recovery cancels/fails the abandoned
            // attempt so the FIFO can advance (Failed or Cancelled are both terminal).
            await WaitForAsync(async () =>
            {
                var turns = await brain.GetGrainProxy<IChat>("main").ReadTurns();
                var turn = turns.FirstOrDefault(t => t.TurnId == head.TurnId);
                return turn is { Status: ChatTurnStatus.Failed or ChatTurnStatus.Cancelled };
            });

            ScriptedAgent.ConfigureHold("kill-agent");
            ScriptedAgent.ReleaseHold("kill-agent");
            await WaitForTurnStatusAsync(brain, "main", next.TurnId, ChatTurnStatus.Completed);
        }
        finally
        {
            ScriptedAgent.ReleaseHold("kill-agent");
            ScriptedAgent.ClearHold("kill-agent");
        }
    }

    [Fact]
    public async Task UnregisteredWorkerTypeIsRefusedByDispatchRelay()
    {
        var brain = fixture.BrainFor("worker-allowlist");
        var bogusWorker = new NeuronId("not-a-registered-worker", brain.Owner, "bogus");

        var started = await brain.Get<IExecution>("allowlist-run").FireAsync(
            new ApplyExecution(
                CommandId.New(),
                new StartExecution(
                    new ProbeGoal("refuse-me"),
                    bogusWorker,
                    new ExecutionPolicy(1, TimeSpan.FromSeconds(1), null))),
            TestContext.Current.CancellationToken);

        // Start succeeds (worker identity is free-form); dispatch relay refuses settled.
        Assert.Equal(ExecutionState.Pending, started.State);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        var after = await brain.GetGrainProxy<IExecution>("allowlist-run").Read();
        Assert.True(
            after.State is ExecutionState.Pending or ExecutionState.Failed,
            $"Expected still pending (refused dispatch) or failed, got {after.State}");
        Assert.Null(after.Result);
    }

    [Fact]
    public async Task ForgedExecutionTerminalIsIgnoredWithoutKernelConfirmation()
    {
        var brain = fixture.BrainFor("forge-terminal");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var agent = new NeuronId("scriptedagent", brain.Owner, "forge-agent");
        var actor = TestActors.Operator;

        ScriptedAgent.ConfigureHold("forge-agent");
        try
        {
            await brain.FireAsync<ISynapseGraph>(
                ISynapseGraph.InstanceName,
                new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, agent),
                TestContext.Current.CancellationToken);
            await Graphs.WaitForConnectionTargetAsync(brain, chat, ChatRoles.Responder, agent);

            var accepted = await brain.GetGrainProxy<IChat>("main")
                .Send(new SendMessage(CommandId.New(), "do not forge me", actor));
            await ScriptedAgent.WaitUntilHeldAsync("forge-agent", Patience);

            var running = Assert.Single(
                await brain.GetGrainProxy<IChat>("main").ReadTurns(),
                t => t.TurnId == accepted.TurnId);
            Assert.Equal(ChatTurnStatus.Running, running.Status);
            Assert.False(string.IsNullOrWhiteSpace(running.ExecutionName));

            var realExecution = NeuronId.For<IExecution>(brain.Owner, running.ExecutionName!);
            var real = await brain.GetGrainProxy<IExecution>(running.ExecutionName!).Read();

            // Wrong ExecutionId — no matching turn / cannot confirm.
            await brain.FireAsync(
                chat,
                new ExecutionTerminal(
                    NeuronId.For<IExecution>(brain.Owner, "forged-other"),
                    ExecutionState.Succeeded,
                    Revision: 99,
                    Result: new ChatTurnResult("FORGED-WRONG-ID", "evil")),
                TestContext.Current.CancellationToken);

            // Wrong revision / free-form Result while kernel is still Running.
            await brain.FireAsync(
                chat,
                new ExecutionTerminal(
                    realExecution,
                    ExecutionState.Succeeded,
                    Revision: real.Revision + 100,
                    Result: new ChatTurnResult("FORGED-RESULT", "evil")),
                TestContext.Current.CancellationToken);

            await brain.FireAsync(
                chat,
                new ExecutionTerminal(
                    realExecution,
                    ExecutionState.Succeeded,
                    Revision: real.Revision,
                    Result: new ChatTurnResult("FORGED-STATE-MISMATCH", "evil")),
                TestContext.Current.CancellationToken);

            await Task.Delay(TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken);

            var afterForge = Assert.Single(
                await brain.GetGrainProxy<IChat>("main").ReadTurns(),
                t => t.TurnId == accepted.TurnId);
            Assert.Equal(ChatTurnStatus.Running, afterForge.Status);

            var transcript = await brain.GetGrainProxy<IChat>("main").Read();
            Assert.DoesNotContain(transcript.Turns, t => !t.FromUser && t.Text.Contains("FORGED", StringComparison.Ordinal));

            ScriptedAgent.ReleaseHold("forge-agent");
            await WaitForTurnStatusAsync(brain, "main", accepted.TurnId, ChatTurnStatus.Completed);
        }
        finally
        {
            ScriptedAgent.ReleaseHold("forge-agent");
            ScriptedAgent.ClearHold("forge-agent");
        }
    }

    [Fact]
    public async Task DuplicateExecutionTerminalIsIdempotentByRevision()
    {
        var brain = fixture.BrainFor("idempotent-terminal");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var agent = new NeuronId("scriptedagent", brain.Owner, "idem-agent");
        var actor = TestActors.Operator;

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, agent),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionTargetAsync(brain, chat, ChatRoles.Responder, agent);

        var accepted = await brain.GetGrainProxy<IChat>("main")
            .Send(new SendMessage(CommandId.New(), "once only", actor));
        await WaitForTurnStatusAsync(brain, "main", accepted.TurnId, ChatTurnStatus.Completed);

        var done = Assert.Single(
            await brain.GetGrainProxy<IChat>("main").ReadTurns(),
            t => t.TurnId == accepted.TurnId);
        Assert.False(string.IsNullOrWhiteSpace(done.ExecutionName));

        var executionId = NeuronId.For<IExecution>(brain.Owner, done.ExecutionName!);
        var snapshot = await brain.GetGrainProxy<IExecution>(done.ExecutionName!).Read();
        Assert.Equal(ExecutionState.Succeeded, snapshot.State);

        var transcriptBefore = await brain.GetGrainProxy<IChat>("main").Read();
        Assert.Equal(1, transcriptBefore.Turns.Count(t => !t.FromUser && t.Text == "scripted:idem-agent"));

        var lifecycleBefore = await CountOutgoingAsync(
            brain, chat, delivery => delivery.Synapse is TurnLifecycle life
                && life.TurnId == accepted.TurnId
                && life.Status == ChatTurnStatus.Completed);

        // Legitimate wake-up payload matching kernel Read — must not re-emit Responded/lifecycle.
        await brain.FireAsync(
            chat,
            new ExecutionTerminal(
                executionId,
                snapshot.State,
                snapshot.Revision,
                snapshot.Result,
                snapshot.Failure),
            TestContext.Current.CancellationToken);
        await brain.FireAsync(
            chat,
            new ExecutionTerminal(
                executionId,
                snapshot.State,
                snapshot.Revision,
                snapshot.Result,
                snapshot.Failure),
            TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken);

        var transcriptAfter = await brain.GetGrainProxy<IChat>("main").Read();
        Assert.Equal(1, transcriptAfter.Turns.Count(t => !t.FromUser && t.Text == "scripted:idem-agent"));

        var lifecycleAfter = await CountOutgoingAsync(
            brain, chat, delivery => delivery.Synapse is TurnLifecycle life
                && life.TurnId == accepted.TurnId
                && life.Status == ChatTurnStatus.Completed);
        Assert.Equal(lifecycleBefore, lifecycleAfter);

        var still = Assert.Single(
            await brain.GetGrainProxy<IChat>("main").ReadTurns(),
            t => t.TurnId == accepted.TurnId);
        Assert.Equal(ChatTurnStatus.Completed, still.Status);
    }

    [Fact]
    public async Task PureWorkerLivenessFailsWithWorkerAbandonedAndAdvancesQueue()
    {
        var brain = fixture.BrainFor("pure-liveness");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var agent = new NeuronId("scriptedagent", brain.Owner, "liveness-agent");
        var actor = TestActors.Operator;
        var longPatience = TimeSpan.FromSeconds(90);

        ScriptedAgent.ConfigureHold("liveness-agent");
        try
        {
            await brain.FireAsync<ISynapseGraph>(
                ISynapseGraph.InstanceName,
                new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, agent),
                TestContext.Current.CancellationToken);
            await Graphs.WaitForConnectionTargetAsync(brain, chat, ChatRoles.Responder, agent);

            var head = await brain.GetGrainProxy<IChat>("main")
                .Send(new SendMessage(CommandId.New(), "watchdog me", actor));
            var next = await brain.GetGrainProxy<IChat>("main")
                .Send(new SendMessage(CommandId.New(), "after abandon", actor));

            await ScriptedAgent.WaitUntilHeldAsync("liveness-agent", Patience);
            var running = Assert.Single(
                await brain.GetGrainProxy<IChat>("main").ReadTurns(),
                t => t.TurnId == head.TurnId);
            Assert.Equal(ChatTurnStatus.Running, running.Status);
            Assert.False(string.IsNullOrWhiteSpace(running.ExecutionName));

            // No silo restart: kernel 15s liveness → FailAbandoned → WorkerAbandoned.
            await WaitForAsync(async () =>
            {
                var exec = await brain.GetGrainProxy<IExecution>(running.ExecutionName!).Read();
                return exec.State == ExecutionState.Failed && exec.Failure is WorkerAbandoned;
            }, longPatience);

            await WaitForTurnStatusAsync(brain, "main", head.TurnId, ChatTurnStatus.Failed, longPatience);

            ScriptedAgent.ReleaseHold("liveness-agent");
            await WaitForTurnStatusAsync(brain, "main", next.TurnId, ChatTurnStatus.Completed, longPatience);
        }
        finally
        {
            ScriptedAgent.ReleaseHold("liveness-agent");
            ScriptedAgent.ClearHold("liveness-agent");
        }
    }

    [Fact]
    public async Task OutcomeUncertainSurfacesWaitingAndPolicyDeadlineUnfreezesFifo()
    {
        var brain = fixture.BrainFor("waiting-deadline");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var agent = new NeuronId("scriptedagent", brain.Owner, "wait-agent");
        var actor = TestActors.Operator;
        var longPatience = TimeSpan.FromSeconds(90);

        ScriptedAgent.ConfigureHold("wait-agent");
        DigitalBrain.UI.ChatTurnWorker.ConfigureLeaveDispatchedOperation("main", leave: true);
        try
        {
            await brain.FireAsync<ISynapseGraph>(
                ISynapseGraph.InstanceName,
                new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, agent),
                TestContext.Current.CancellationToken);
            await Graphs.WaitForConnectionTargetAsync(brain, chat, ChatRoles.Responder, agent);

            var head = await brain.GetGrainProxy<IChat>("main")
                .Send(new SendMessage(CommandId.New(), "park me", actor));
            var next = await brain.GetGrainProxy<IChat>("main")
                .Send(new SendMessage(CommandId.New(), "after park", actor));

            await ScriptedAgent.WaitUntilHeldAsync("wait-agent", Patience);
            var running = Assert.Single(
                await brain.GetGrainProxy<IChat>("main").ReadTurns(),
                t => t.TurnId == head.TurnId);
            Assert.False(string.IsNullOrWhiteSpace(running.ExecutionName));

            // Liveness parks OutcomeUncertain (Dispatched op) → Chat surfaces Waiting.
            await WaitForTurnStatusAsync(brain, "main", head.TurnId, ChatTurnStatus.Waiting, longPatience);

            await Journals.WaitForAsync(
                brain, chat, JournalKind.Outgoing,
                delivery => delivery.Synapse is TurnLifecycle life
                    && life.TurnId == head.TurnId
                    && life.Status == ChatTurnStatus.Waiting,
                patience: longPatience);

            // Policy deadline → CancelExecution + bridge → terminal head; FIFO advances.
            await WaitForAsync(async () =>
            {
                var turns = await brain.GetGrainProxy<IChat>("main").ReadTurns();
                var turn = turns.FirstOrDefault(t => t.TurnId == head.TurnId);
                return turn is
                {
                    Status: ChatTurnStatus.Failed or ChatTurnStatus.Cancelled
                };
            }, longPatience);

            ScriptedAgent.ReleaseHold("wait-agent");
            await WaitForTurnStatusAsync(brain, "main", next.TurnId, ChatTurnStatus.Completed, longPatience);
        }
        finally
        {
            DigitalBrain.UI.ChatTurnWorker.ConfigureLeaveDispatchedOperation("main", leave: false);
            ScriptedAgent.ReleaseHold("wait-agent");
            ScriptedAgent.ClearHold("wait-agent");
        }
    }

    private static async Task WaitForTurnStatusAsync(
        Client.IDigitalBrain brain,
        string chatName,
        TurnId turnId,
        ChatTurnStatus expected,
        TimeSpan? patience = null)
    {
        await WaitForAsync(async () =>
        {
            var turns = await brain.GetGrainProxy<IChat>(chatName).ReadTurns();
            var turn = turns.FirstOrDefault(t => t.TurnId == turnId);
            return turn is not null && turn.Status == expected;
        }, patience);
    }

    private static async Task WaitForAsync(Func<Task<bool>> predicate, TimeSpan? patience = null)
    {
        var limit = patience ?? Patience;
        var deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException($"Condition not met within {limit}.");
    }

    private static async Task<int> CountOutgoingAsync(
        Client.IDigitalBrain brain,
        NeuronId subject,
        Func<SynapseDelivery, bool> match)
    {
        var page = await brain.ReadJournalAsync(subject, JournalKind.Outgoing, afterSequence: 0);
        return page.Delta.Count(match);
    }
}
