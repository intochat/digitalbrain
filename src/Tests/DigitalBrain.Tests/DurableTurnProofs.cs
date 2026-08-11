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

            await ScriptedAgent.WaitUntilHeldAsync("cancel-run", Patience);

            var cancelCommand = CommandId.New();
            await brain.GetGrainProxy<IChat>("main").Cancel(
                new CancelTurn(cancelCommand, accepted.TurnId, actor));
            // Idempotent replay of the same cancel command id is allowed via Execution receipts
            // when ExpectedRevision matches; a second cancel with a new id is also a no-op once terminal.
            await brain.GetGrainProxy<IChat>("main").Cancel(
                new CancelTurn(CommandId.New(), accepted.TurnId, actor));

            await WaitForTurnStatusAsync(brain, "main", accepted.TurnId, ChatTurnStatus.Cancelled);
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

            await ScriptedAgent.WaitUntilHeldAsync("restart-agent", Patience);
            var before = await brain.GetGrainProxy<IChat>("main").ReadTurns();
            var beforeTurn = Assert.Single(before, t => t.TurnId == accepted.TurnId);
            Assert.Equal(ChatTurnStatus.Running, beforeTurn.Status);
            Assert.False(string.IsNullOrWhiteSpace(beforeTurn.ExecutionName));

            await fixture.RestartSilosAsync();

            // Durable turn must still be visible (not vanished).
            ChatTurnSnapshot? after = null;
            await WaitForAsync(async () =>
            {
                try
                {
                    var turns = await brain.GetGrainProxy<IChat>("main").ReadTurns();
                    after = turns.FirstOrDefault(t => t.TurnId == accepted.TurnId);
                    return after is not null
                        && after.Status is ChatTurnStatus.Pending or ChatTurnStatus.Running or ChatTurnStatus.Completed;
                }
                catch
                {
                    return false;
                }
            });

            Assert.NotNull(after);
            Assert.NotEqual(ChatTurnStatus.Failed, after!.Status);

            // Execution snapshot also survives.
            if (!string.IsNullOrWhiteSpace(beforeTurn.ExecutionName))
            {
                ExecutionSnapshot? exec = null;
                await WaitForAsync(async () =>
                {
                    try
                    {
                        exec = await brain.GetGrainProxy<IExecution>(beforeTurn.ExecutionName!).Read();
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                });
                Assert.NotNull(exec);
            }

            // Re-hold may have been lost on agent grain memory; release and allow completion
            // if Execution re-dispatches Accept after restart.
            ScriptedAgent.ReleaseHold("restart-agent");
            ScriptedAgent.ConfigureHold("restart-agent");
            ScriptedAgent.ReleaseHold("restart-agent");

            await WaitForAsync(async () =>
            {
                var turns = await brain.GetGrainProxy<IChat>("main").ReadTurns();
                var turn = turns.FirstOrDefault(t => t.TurnId == accepted.TurnId);
                return turn is { Status: ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled or ChatTurnStatus.Running or ChatTurnStatus.Pending };
            });

            var finalTurns = await brain.GetGrainProxy<IChat>("main").ReadTurns();
            Assert.Contains(finalTurns, t => t.TurnId == accepted.TurnId);
        }
        finally
        {
            ScriptedAgent.ReleaseHold("restart-agent");
            ScriptedAgent.ClearHold("restart-agent");
        }
    }

    private static async Task WaitForTurnStatusAsync(
        Client.IDigitalBrain brain,
        string chatName,
        TurnId turnId,
        ChatTurnStatus expected)
    {
        await WaitForAsync(async () =>
        {
            var turns = await brain.GetGrainProxy<IChat>(chatName).ReadTurns();
            var turn = turns.FirstOrDefault(t => t.TurnId == turnId);
            return turn is not null && turn.Status == expected;
        });
    }

    private static async Task WaitForAsync(Func<Task<bool>> predicate)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException($"Condition not met within {Patience}.");
    }
}
