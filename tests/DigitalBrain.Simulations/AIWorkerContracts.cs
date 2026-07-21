using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using DigitalBrain.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class AIWorkerContracts
{
    [Fact(DisplayName = "GroupChat accepts a real Task before model work and completes one terminal Lockstep superstep")]
    public async Task GroupChatTaskRunReturnsBeforeTheModelAndCompletesOneTerminalSuperstep()
    {
        var cluster = await StartWorkerClusterAsync();
        AIWorkerLogProvider.Clear();

        var owner = new OwnerId("ai-worker-happy-path");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driverId = NeuronId.For<TaskDriver>(owner, "ai-worker-driver");
        var driver = cluster.Client.GetGrain<ITaskDriver>(driverId.ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var gate = AIWorkerGate.Prepare(owner, "complete the supervised run", "terminal answer");

        try
        {
            _ = await task.StartAsync(new(
                    CommandId.New(),
                    new AIWorkerGoal("complete the supervised run"),
                    workerId,
                    new TaskPolicy(1, TimeSpan.Zero, null)))
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            var running = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
            try
            {
                await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            }
            catch (TimeoutException failure)
            {
                throw new TimeoutException(
                    $"The workflow runner did not reach the model. {string.Join(Environment.NewLine, AIWorkerLogProvider.Messages)}",
                    failure);
            }

            Assert.Equal(0, running.Revision);
            Assert.Equal(1, gate.EntryCount);

            gate.Release();

            var succeeded = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);
            var result = Assert.IsType<AIWorkerResult>(succeeded.Result);

            Assert.Equal("terminal answer", result.Answer);
            Assert.True(result.OutputWasReadOnly);
            Assert.Equal(0, succeeded.Revision);
            Assert.Null(succeeded.ActiveAttempt);
            Assert.Equal(1, gate.EntryCount);
        }
        finally
        {
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "completion delegation is minted only after terminal output for the current run")]
    public async Task CompletionDelegationIsMintedOnlyAfterTerminalOutputForTheCurrentRun()
    {
        var cluster = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-just-in-time-completion");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var gate = AIWorkerGate.Prepare(owner, "authorize completion late", "late authorization answer");

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("authorize completion late"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            var whileModelBlocked = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);

            Assert.DoesNotContain(
                whileModelBlocked.Delta,
                delivery => delivery.Synapse is CapabilityRequested request
                    && request.Target == workerId);

            gate.Release();
            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);

            var completed = await ReadJournalUntilAsync(
                probe,
                workerId,
                journal =>
                {
                    var request = journal.Delta.SingleOrDefault(delivery =>
                        delivery.Synapse is CapabilityRequested capability
                        && capability.Target == workerId);

                    return request is not null
                        && journal.Delta.Any(delivery =>
                            delivery.Synapse is CapabilityCompleted outcome
                            && outcome.Request == request.SynapseId);
                });
            var completionRequest = Assert.Single(
                completed.Delta,
                delivery => delivery.Synapse is CapabilityRequested request
                    && request.Target == workerId);

            Assert.Single(
                completed.Delta,
                delivery => delivery.Synapse is CapabilityCompleted outcome
                    && outcome.Request == completionRequest.SynapseId);
        }
        finally
        {
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "GroupChat copies mutable input and read-only output mapping boundaries")]
    public async Task GroupChatCopiesBothTaskMappingBoundaries()
    {
        var cluster = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-mapping-copies");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var gate = AIWorkerGate.Prepare(
            owner,
            "copy the mapping boundary",
            "copied answer",
            mutateSourceDuringEnumeration: true);

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("copy the mapping boundary"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.True(gate.SourceWasMutated);
            Assert.Equal("copy the mapping boundary", gate.ObservedInput[0].Text);

            gate.Release();

            var succeeded = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);
            var result = Assert.IsType<AIWorkerResult>(succeeded.Result);

            Assert.Equal("copied answer", result.Answer);
            Assert.True(result.OutputWasReadOnly);
        }
        finally
        {
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "a conflicting real Task cannot replace an active GroupChat run")]
    public async Task ConflictingTaskCannotReplaceTheActiveRun()
    {
        var cluster = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-active-conflict");
        var firstTaskId = NeuronId.For<ITask>(owner, "first-task");
        var secondTaskId = NeuronId.For<ITask>(owner, "second-task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var first = new TaskTestClient(firstTaskId, driver);
        var second = new TaskTestClient(secondTaskId, driver);
        var gate = AIWorkerGate.Prepare(
            owner,
            "first run",
            "first answer",
            requirePromptMatch: false);

        try
        {
            _ = await first.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("first run"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            var firstRunning = await ReadUntilAsync(first, snapshot => snapshot.State == TaskState.Running);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            var secondStarted = await second.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("second run"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            var secondPending = await second.ReadAsync();

            Assert.Equal(TaskState.Running, firstRunning.State);
            Assert.Equal(TaskState.Pending, secondPending.State);
            Assert.Equal(secondStarted.ActiveAttempt, secondPending.ActiveAttempt);
            Assert.Equal(secondStarted.Revision, secondPending.Revision);
            Assert.Equal(1, gate.EntryCount);
            Assert.Equal(1, gate.MaximumConcurrency);

            gate.Release();
            _ = await ReadUntilAsync(first, snapshot => snapshot.State == TaskState.Succeeded);
            Assert.Equal(1, gate.EntryCount);
        }
        finally
        {
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "direct Respond is rejected before session or model mutation while supervised work is active")]
    public async Task DirectRespondIsRejectedWhileASupervisedRunIsActive()
    {
        var cluster = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-direct-exclusion");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var gate = AIWorkerGate.Prepare(owner, "supervised run", "supervised answer");

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("supervised run"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var before = await probe.ReadDirectStateAsync(workerId);

            var direct = probe.RespondAsync(
                workerId,
                [new ChatMessage(ChatRole.User, "must not enter")]);
            var firstCompleted = await Task.WhenAny(direct, gate.SecondEntry)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Same(direct, firstCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => direct);
            Assert.Equal(before, await probe.ReadDirectStateAsync(workerId));
            Assert.Equal(1, gate.EntryCount);

            gate.Release();
            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);
        }
        finally
        {
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "a wrong caller cannot exploit the active Task's duplicate Accept cursor")]
    public async Task WrongCallerCannotReplayTheActiveTasksAccept()
    {
        var cluster = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-wrong-duplicate-caller");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var wrongId = NeuronId.For<AIWorkerProbe>(owner, "wrong");
        var wrong = cluster.Client.GetGrain<IAIWorkerProbe>(wrongId.ToGrainId());
        var gate = AIWorkerGate.Prepare(owner, "real task run", "real task answer");

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("real task run"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            var running = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var request = new AttemptRequest(
                taskId,
                workerId,
                running.ActiveAttempt!.Value,
                running.Revision,
                new AIWorkerGoal("real task run"));
            var before = await wrong.ReadWorkerStateAsync(workerId);

            await Assert.ThrowsAsync<NeuronAuthorizationException>(() =>
                wrong.AcceptAsync(workerId, request));

            Assert.Equal(before, await wrong.ReadWorkerStateAsync(workerId));
            var outgoing = await wrong.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.Single(outgoing.Delta, delivery => delivery.Synapse is AttemptAccepted);
            Assert.Equal(1, gate.EntryCount);

            gate.Release();
            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);
        }
        finally
        {
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "a same-owner non-Task neuron cannot impersonate an initiating Task")]
    public async Task NonTaskNeuronCannotInitiateWorkerAcceptance()
    {
        var cluster = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-task-type-fence");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var probeId = NeuronId.For<AIWorkerProbe>(owner, "probe");
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(probeId.ToGrainId());
        var request = new AttemptRequest(
            probeId,
            workerId,
            new AttemptId(Guid.NewGuid()),
            Revision: 0,
            new AIWorkerGoal("impersonated task"));

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => probe.AcceptAsync(workerId, request));
            Assert.Empty(await probe.ReadWorkerStateAsync(workerId));
            var outgoing = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.DoesNotContain(outgoing.Delta, delivery => delivery.Synapse is AttemptAccepted);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "GroupChat rejects an empty participant snapshot before accepting a Task")]
    public async Task EmptyParticipantSnapshotIsRejectedBeforeAcceptance()
    {
        var cluster = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-empty-participants");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<IEmptyTaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());

        try
        {
            var started = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("cannot run"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));

            Assert.Equal(TaskState.Pending, (await task.ReadAsync()).State);
            Assert.NotNull(started.ActiveAttempt);
            var outgoing = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.DoesNotContain(outgoing.Delta, delivery => delivery.Synapse is AttemptAccepted);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "GroupChat rejects a foreign participant before accepting a Task")]
    public async Task ForeignParticipantIsRejectedBeforeAcceptance()
    {
        var cluster = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-foreign-participant");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<IForeignParticipantTaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("cannot delegate"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));

            Assert.Equal(TaskState.Pending, (await task.ReadAsync()).State);
            var outgoing = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.DoesNotContain(outgoing.Delta, delivery => delivery.Synapse is AttemptAccepted);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "a failed worker acceptance commit cannot leak an orphan ActiveRun")]
    public async Task FailedAcceptanceCommitRollsBackWorkerState()
    {
        var journals = new AIWorkerJournalStorageProvider();
        var cluster = await StartWorkerClusterAsync(journals);
        var owner = new OwnerId("ai-worker-accept-write-rollback");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var gate = AIWorkerGate.Prepare(owner, "retry after rollback", "recovered answer");

        try
        {
            journals.FailWriteAfter(
                workerId.ToGrainId(),
                completedWritesBeforeFailure: 1,
                "Expected worker acceptance commit failure.");

            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("retry after rollback"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            Assert.True(
                journals.FiredFailures(workerId.ToGrainId()) == 1,
                $"Expected one injected failure after two worker writes; observed {journals.Writes(workerId.ToGrainId())} writes.");
            journals.ClearFailure(workerId.ToGrainId());

            Assert.Equal(TaskState.Pending, (await task.ReadAsync()).State);
            Assert.Empty(await probe.ReadWorkerStateAsync(workerId));
            var afterFailure = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.DoesNotContain(afterFailure.Delta, delivery => delivery.Synapse is AttemptAccepted);

            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.NotEmpty(await probe.ReadWorkerStateAsync(workerId));

            gate.Release();
            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);
        }
        finally
        {
            journals.ClearFailure(workerId.ToGrainId());
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "a failed terminal adoption commit cannot leak a cleared ActiveRun")]
    public async Task FailedTerminalAdoptionCommitRollsBackWorkerState()
    {
        var journals = new AIWorkerJournalStorageProvider();
        var cluster = await StartWorkerClusterAsync(journals);
        AIWorkerLogProvider.Clear();
        var owner = new OwnerId("ai-worker-adoption-write-rollback");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var gate = AIWorkerGate.Prepare(owner, "fail adoption", "unadopted answer");

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("fail adoption"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var before = await probe.ReadWorkerStateAsync(workerId);

            journals.FailWriteAfter(
                workerId.ToGrainId(),
                completedWritesBeforeFailure: 3,
                "Expected terminal adoption commit failure.");
            gate.Release();

            await WaitUntilAsync(
                () => journals.FiredFailures(workerId.ToGrainId()) == 1,
                "The terminal adoption write failure did not fire.");

            Assert.Equal(before, await probe.ReadWorkerStateAsync(workerId));
            Assert.Equal(TaskState.Running, (await task.ReadAsync()).State);
            var outgoing = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.DoesNotContain(outgoing.Delta, delivery => delivery.Synapse is AttemptSucceeded);
        }
        finally
        {
            journals.ClearFailure(workerId.ToGrainId());
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "checkpoint storage commits before GroupChat adopts terminal output")]
    public async Task CheckpointWriteCompletesBeforeTerminalAdoption()
    {
        var journals = new AIWorkerJournalStorageProvider();
        var cluster = await StartWorkerClusterAsync(journals);
        var owner = new OwnerId("ai-worker-checkpoint-order");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var model = AIWorkerGate.Prepare(owner, "checkpoint ordering", "ordered answer");
        AIWorkerWriteGate? checkpoint = null;

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("checkpoint ordering"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            var running = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
            await model.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var checkpointGrain = CheckpointGrain(
                taskId,
                workerId,
                running.ActiveAttempt!.Value);
            var completedBefore = journals.CompletedWrites(checkpointGrain);
            checkpoint = journals.BlockNextWrite(checkpointGrain);

            model.Release();
            await checkpoint.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Assert.Equal(TaskState.Running, (await task.ReadAsync()).State);
            var whileBlocked = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.DoesNotContain(whileBlocked.Delta, delivery => delivery.Synapse is AttemptSucceeded);
            Assert.Equal(completedBefore, journals.CompletedWrites(checkpointGrain));

            checkpoint.Release();

            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);
            Assert.True(journals.CompletedWrites(checkpointGrain) > completedBefore);
        }
        finally
        {
            checkpoint?.Release();
            model.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "a raw same-owner runner is rejected before semantic target entry")]
    public async Task RawRunnerIsRejectedBeforeSemanticTargetEntry()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("ai-worker-raw-runner");
        var target = NeuronId.For<RawCapabilityTarget>(owner, "probe");
        var runner = SimulationCluster.Grains.GetGrain<IRawWorkflowRunner>(
            IdSpan.Create($"{owner.Value}/runner"));

        var failure = await Record.ExceptionAsync(() => runner.InvokeAsync(target));

        Assert.Equal(0, RawCapabilityTargetObservations.EntryCount(target));
        Assert.IsType<NeuronAuthorizationException>(failure);
    }

    [Fact(DisplayName = "a deliberate client entry point remains callable without a reified request")]
    public async Task ClientEntryPointRemainsCallableWithoutAReifiedRequest()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("kernel-client-entry");
        var target = NeuronId.For<KernelClientEntryTarget>(owner, "probe");
        var probe = SimulationCluster.Grains.GetGrain<IKernelClientEntryProbe>(target.ToGrainId());

        Assert.Equal(1, await probe.EnterAsync());
    }

    private static async Task<TaskSnapshot> ReadUntilAsync(
        TaskTestClient task,
        Func<TaskSnapshot, bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (true)
        {
            var snapshot = await task.ReadAsync();

            if (condition(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private static async Task<JournalRead> ReadJournalUntilAsync(
        IAIWorkerProbe probe,
        NeuronId worker,
        Func<JournalRead, bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (true)
        {
            var journal = await probe.ReadJournalAsync(worker, JournalKind.Outgoing);

            if (condition(journal))
            {
                return journal;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string timeoutMessage)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }
            catch (OperationCanceledException failure) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException(timeoutMessage, failure);
            }
        }
    }

    private static GrainId CheckpointGrain(
        NeuronId task,
        NeuronId worker,
        AttemptId attempt)
    {
        var source = Encoding.UTF8.GetBytes($"v1\n{worker}\n{task}\n{attempt.Value:D}");
        var hash = Convert.ToHexStringLower(SHA256.HashData(source));

        return GrainId.Create(
            "ai-workflow-checkpoint",
            $"{worker.GrainKey}/workflow-checkpoint/{hash}");
    }

    private static async Task<InProcessTestCluster> StartWorkerClusterAsync(
        IJournalStorageProvider? journalStorage = null)
    {
        var builder = new InProcessTestClusterBuilder(1);

        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddDigitalBrain("ai-worker-contracts");
            AIModule.Configure(silo);
            silo.UseInMemoryReminderService();
            silo.Services.AddSingleton<IJournalStorageProvider>(
                journalStorage ?? new VolatileJournalStorageProvider());
            silo.Services.AddSingleton<ILoggerProvider>(AIWorkerLogProvider.Instance);
        });
        builder.ConfigureClient(client =>
        {
            client.Services.AddSerializer(serializer => serializer.AddJsonSerializer(
                type => type == typeof(ChatMessage) || type == typeof(ChatResponse)));
        });

        var cluster = builder.Build();
        await cluster.DeployAsync();

        return cluster;
    }
}

internal sealed class AIWorkerJournalStorageProvider : IJournalStorageProvider
{
    private readonly VolatileJournalStorageProvider _inner = new();
    private readonly Dictionary<JournalId, InjectedFailure> _failures = [];
    private readonly ConcurrentDictionary<JournalId, int> _firedFailures = new();
    private readonly ConcurrentDictionary<JournalId, int> _writes = new();
    private readonly ConcurrentDictionary<JournalId, int> _completedWrites = new();
    private readonly ConcurrentDictionary<JournalId, AIWorkerWriteGate> _blockedWrites = new();
    private readonly object _failureLock = new();

    public IJournalStorage CreateStorage(JournalId journalId)
        => new FaultingStorage(this, journalId, _inner.CreateStorage(journalId));

    internal void FailWriteAfter(
        GrainId grain,
        int completedWritesBeforeFailure,
        string message)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completedWritesBeforeFailure);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        lock (_failureLock)
        {
            _failures[JournalId.FromGrainId(grain)] = new(completedWritesBeforeFailure, message);
        }
    }

    internal void ClearFailure(GrainId grain)
    {
        lock (_failureLock)
        {
            _failures.Remove(JournalId.FromGrainId(grain));
        }
    }

    internal int FiredFailures(GrainId grain)
        => _firedFailures.GetValueOrDefault(JournalId.FromGrainId(grain));

    internal int Writes(GrainId grain)
        => _writes.GetValueOrDefault(JournalId.FromGrainId(grain));

    internal int CompletedWrites(GrainId grain)
        => _completedWrites.GetValueOrDefault(JournalId.FromGrainId(grain));

    internal AIWorkerWriteGate BlockNextWrite(GrainId grain)
    {
        var gate = new AIWorkerWriteGate();

        if (!_blockedWrites.TryAdd(JournalId.FromGrainId(grain), gate))
        {
            throw new InvalidOperationException($"Journal '{grain}' already has a blocked write.");
        }

        return gate;
    }

    private void BeforeWrite(JournalId journalId)
    {
        _writes.AddOrUpdate(journalId, 1, static (_, count) => count + 1);

        lock (_failureLock)
        {
            if (!_failures.TryGetValue(journalId, out var failure))
            {
                return;
            }

            if (failure.CompletedWritesBeforeFailure > 0)
            {
                _failures[journalId] = failure with
                {
                    CompletedWritesBeforeFailure = failure.CompletedWritesBeforeFailure - 1,
                };
                return;
            }

            _failures.Remove(journalId);
            _firedFailures.AddOrUpdate(journalId, 1, static (_, count) => count + 1);
            throw new InvalidOperationException(failure.Message);
        }
    }

    private async ValueTask WaitIfBlockedAsync(
        JournalId journalId,
        CancellationToken cancellationToken)
    {
        if (_blockedWrites.TryRemove(journalId, out var gate))
        {
            await gate.BlockAsync(cancellationToken);
        }
    }

    private void AfterWrite(JournalId journalId)
        => _completedWrites.AddOrUpdate(journalId, 1, static (_, count) => count + 1);

    private sealed record InjectedFailure(int CompletedWritesBeforeFailure, string Message);

    private sealed class FaultingStorage(
        AIWorkerJournalStorageProvider owner,
        JournalId journalId,
        IJournalStorage inner) : IJournalStorage
    {
        public bool IsCompactionRequested => inner.IsCompactionRequested;

        public async ValueTask AppendAsync(
            ReadOnlySequence<byte> value,
            CancellationToken cancellationToken)
        {
            owner.BeforeWrite(journalId);
            await owner.WaitIfBlockedAsync(journalId, cancellationToken);
            await inner.AppendAsync(value, cancellationToken);
            owner.AfterWrite(journalId);
        }

        public async ValueTask ReplaceAsync(
            ReadOnlySequence<byte> value,
            CancellationToken cancellationToken)
        {
            owner.BeforeWrite(journalId);
            await owner.WaitIfBlockedAsync(journalId, cancellationToken);
            await inner.ReplaceAsync(value, cancellationToken);
            owner.AfterWrite(journalId);
        }

        public ValueTask<bool> CreateIfNotExistsAsync(
            IReadOnlyDictionary<string, string>? metadata,
            CancellationToken cancellationToken)
            => inner.CreateIfNotExistsAsync(metadata, cancellationToken);

        public ValueTask DeleteAsync(CancellationToken cancellationToken)
            => inner.DeleteAsync(cancellationToken);

        public ValueTask<IJournalMetadata?> GetMetadataAsync(CancellationToken cancellationToken)
            => inner.GetMetadataAsync(cancellationToken);

        public ValueTask ReadAsync(
            IJournalStorageConsumer consumer,
            CancellationToken cancellationToken)
            => inner.ReadAsync(consumer, cancellationToken);

        public ValueTask<IJournalMetadata?> UpdateMetadataAsync(
            IReadOnlyDictionary<string, string>? metadata,
            IEnumerable<string>? tagsToRemove,
            string? eTag,
            CancellationToken cancellationToken)
            => inner.UpdateMetadataAsync(metadata, tagsToRemove, eTag, cancellationToken);
    }
}

internal sealed class AIWorkerWriteGate
{
    private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Entered => _entered.Task;

    internal async ValueTask BlockAsync(CancellationToken cancellationToken)
    {
        _entered.TrySetResult(true);
        await _release.Task.WaitAsync(cancellationToken);
    }

    internal void Release() => _release.TrySetResult(true);
}

internal sealed class AIWorkerLogProvider : ILoggerProvider
{
    private static readonly ConcurrentQueue<string> Recorded = new();

    internal static AIWorkerLogProvider Instance { get; } = new();

    internal static IReadOnlyList<string> Messages => [.. Recorded];

    internal static void Clear()
    {
        while (Recorded.TryDequeue(out _))
        {
        }
    }

    public ILogger CreateLogger(string categoryName) => new AIWorkerLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class AIWorkerLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Recorded.Enqueue($"{category}: {formatter(state, exception)}{Environment.NewLine}{exception}");
            }
        }
    }
}

[GenerateSerializer]
[Alias("db.test.ai-worker-goal")]
internal sealed record AIWorkerGoal([property: Id(0)] string Prompt) : Goal;

[GenerateSerializer]
[Alias("db.test.ai-worker-result")]
internal sealed record AIWorkerResult(
    [property: Id(0)] string Answer,
    [property: Id(1)] bool OutputWasReadOnly) : Result;

[Alias("db.test.ai-worker-model")]
internal interface IAIWorkerModel : ILLM;

internal sealed class AIWorkerModel : Neuron, IAIWorkerModel
{
    public Task<ChatResponse> RespondAsync(IReadOnlyList<ChatMessage> messages)
        => AIWorkerGate.For(Id.Owner).RespondAsync(messages);
}

[Alias("db.test.task-group-chat")]
internal interface ITaskGroupChat : IGroupChat
{
    [Alias("ReadDirectState")]
    Task<byte[]> ReadDirectStateAsync();

    [Alias("ReadWorkerState")]
    Task<byte[]> ReadWorkerStateAsync();
}

internal sealed class TaskGroupChat : GroupChat, ITaskGroupChat
{
    private const string DirectStateName = "ai.group-chat.session";
    private const string WorkerStateName = "ai.group-chat.worker";

    protected override IReadOnlyList<Participant> Participants => [Participant<IAIWorkerModel>()];

    protected override IReadOnlyList<ChatMessage> CreateMessages(Goal goal)
    {
        var request = Assert.IsType<AIWorkerGoal>(goal);

        return AIWorkerGate.For(Id.Owner).SourceMessages(request.Prompt);
    }

    protected override Result CreateResult(IReadOnlyList<ChatMessage> messages)
        => new AIWorkerResult(
            messages.Last(message => message.Role == ChatRole.Assistant).Text,
            messages is not IList<ChatMessage> mutable || mutable.IsReadOnly);

    public override Task ContinueAsync(AttemptCursor cursor) => throw new NotSupportedException();

    public override Task CancelAsync(AttemptCursor cursor) => throw new NotSupportedException();

    public Task<byte[]> ReadDirectStateAsync()
        => Task.FromResult(ReadState(DirectStateName));

    public Task<byte[]> ReadWorkerStateAsync()
        => Task.FromResult(ReadState(WorkerStateName));

    private byte[] ReadState(string name)
        => ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(name).Value?.ToArray() ?? [];
}

[Alias("db.test.empty-task-group-chat")]
internal interface IEmptyTaskGroupChat : IGroupChat;

internal sealed class EmptyTaskGroupChat : GroupChat, IEmptyTaskGroupChat
{
    protected override IReadOnlyList<Participant> Participants => [];

    protected override IReadOnlyList<ChatMessage> CreateMessages(Goal goal)
        => [new ChatMessage(ChatRole.User, "cannot run")];

    protected override Result CreateResult(IReadOnlyList<ChatMessage> messages)
        => throw new NotSupportedException();

    public override Task ContinueAsync(AttemptCursor cursor) => throw new NotSupportedException();

    public override Task CancelAsync(AttemptCursor cursor) => throw new NotSupportedException();
}

[Alias("db.test.foreign-participant-task-group-chat")]
internal interface IForeignParticipantTaskGroupChat : IGroupChat;

internal sealed class ForeignParticipantTaskGroupChat : GroupChat, IForeignParticipantTaskGroupChat
{
    protected override IReadOnlyList<Participant> Participants =>
    [
        new Participant<IAIWorkerModel>(NeuronId.For<IAIWorkerModel>(
            new OwnerId($"{Id.Owner.Value}-foreign"),
            Id.Name))
    ];

    protected override IReadOnlyList<ChatMessage> CreateMessages(Goal goal)
        => [new ChatMessage(ChatRole.User, "cannot delegate")];

    protected override Result CreateResult(IReadOnlyList<ChatMessage> messages)
        => throw new NotSupportedException();

    public override Task ContinueAsync(AttemptCursor cursor) => throw new NotSupportedException();

    public override Task CancelAsync(AttemptCursor cursor) => throw new NotSupportedException();
}

[Alias("db.test.ai-worker-probe")]
[ClientEntryPoint]
internal interface IAIWorkerProbe : INeuron
{
    [Alias("Accept")]
    Task AcceptAsync(NeuronId worker, AttemptRequest request);

    [Alias("Respond")]
    Task<ChatResponse> RespondAsync(NeuronId worker, IReadOnlyList<ChatMessage> messages);

    [Alias("ReadDirectState")]
    Task<byte[]> ReadDirectStateAsync(NeuronId worker);

    [Alias("ReadWorkerState")]
    Task<byte[]> ReadWorkerStateAsync(NeuronId worker);

    [Alias("ReadJournal")]
    Task<JournalRead> ReadJournalAsync(NeuronId worker, JournalKind kind);
}

internal sealed class AIWorkerProbe : Neuron, IAIWorkerProbe
{
    public Task AcceptAsync(NeuronId worker, AttemptRequest request)
        => GrainFactory.GetGrain<IWorker>(worker.ToGrainId()).AcceptAsync(request);

    public Task<ChatResponse> RespondAsync(NeuronId worker, IReadOnlyList<ChatMessage> messages)
        => GrainFactory.GetGrain<IAgent>(worker.ToGrainId()).RespondAsync(messages);

    public Task<byte[]> ReadDirectStateAsync(NeuronId worker)
        => GrainFactory.GetGrain<ITaskGroupChat>(worker.ToGrainId()).ReadDirectStateAsync();

    public Task<byte[]> ReadWorkerStateAsync(NeuronId worker)
        => GrainFactory.GetGrain<ITaskGroupChat>(worker.ToGrainId()).ReadWorkerStateAsync();

    public Task<JournalRead> ReadJournalAsync(NeuronId worker, JournalKind kind)
        => GrainFactory.GetGrain<INeuron>(worker.ToGrainId()).ReadJournalAsync(kind, afterSequence: 0);
}

internal sealed class AIWorkerGate
{
    private static readonly ConcurrentDictionary<OwnerId, AIWorkerGate> Gates = new();
    private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _secondEntry = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _answer;
    private readonly bool _mutateSourceDuringEnumeration;
    private readonly bool _requirePromptMatch;
    private int _entries;
    private int _active;
    private int _maximumConcurrency;
    private BoundaryMutationMessages? _source;
    private ChatMessage[] _observedInput = [];

    private AIWorkerGate(
        string prompt,
        string answer,
        bool mutateSourceDuringEnumeration,
        bool requirePromptMatch)
    {
        Prompt = prompt;
        _answer = answer;
        _mutateSourceDuringEnumeration = mutateSourceDuringEnumeration;
        _requirePromptMatch = requirePromptMatch;
    }

    internal string Prompt { get; }

    internal Task Entered => _entered.Task;

    internal Task SecondEntry => _secondEntry.Task;

    internal int EntryCount => Volatile.Read(ref _entries);

    internal int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

    internal IReadOnlyList<ChatMessage> ObservedInput => _observedInput;

    internal bool SourceWasMutated => _source?.WasMutated ?? false;

    internal static AIWorkerGate Prepare(
        OwnerId owner,
        string prompt,
        string answer,
        bool mutateSourceDuringEnumeration = false,
        bool requirePromptMatch = true)
    {
        var gate = new AIWorkerGate(
            prompt,
            answer,
            mutateSourceDuringEnumeration,
            requirePromptMatch);
        Gates[owner] = gate;

        return gate;
    }

    internal static AIWorkerGate For(OwnerId owner)
        => Gates.TryGetValue(owner, out var gate)
            ? gate
            : throw new InvalidOperationException($"No AI worker gate is prepared for owner '{owner}'.");

    internal IReadOnlyList<ChatMessage> SourceMessages(string prompt)
    {
        if (_requirePromptMatch)
        {
            Assert.Equal(Prompt, prompt);
        }

        if (!_mutateSourceDuringEnumeration)
        {
            return [new ChatMessage(ChatRole.User, prompt)];
        }

        _source = new BoundaryMutationMessages(prompt);
        return _source;
    }

    internal async Task<ChatResponse> RespondAsync(IReadOnlyList<ChatMessage> messages)
    {
        _observedInput = [.. messages.Select(message => message.Clone())];
        var entry = Interlocked.Increment(ref _entries);
        var active = Interlocked.Increment(ref _active);
        UpdateMaximum(active);

        if (entry == 1)
        {
            _entered.TrySetResult(true);
        }
        else
        {
            _secondEntry.TrySetResult(true);
        }

        try
        {
            await _release.Task;
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, _answer));
    }

    internal void Release() => _release.TrySetResult(true);

    private void UpdateMaximum(int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref _maximumConcurrency);

            if (candidate <= current
                || Interlocked.CompareExchange(ref _maximumConcurrency, candidate, current) == current)
            {
                return;
            }
        }
    }

    private sealed class BoundaryMutationMessages(string prompt) : IReadOnlyList<ChatMessage>
    {
        private readonly ChatMessage _first = new(ChatRole.User, prompt);
        private readonly ChatMessage _second = new(ChatRole.User, "boundary sentinel");

        internal bool WasMutated { get; private set; }

        public int Count => 2;

        public ChatMessage this[int index] => index switch
        {
            0 => _first,
            1 => _second,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public IEnumerator<ChatMessage> GetEnumerator()
        {
            yield return _first;
            _first.Contents.Clear();
            _first.Contents.Add(new TextContent("mutated after first element enumeration"));
            WasMutated = true;
            yield return _second;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

[Alias("db.test.raw-workflow-runner")]
internal interface IRawWorkflowRunner : IGrainWithStringKey
{
    [Alias("Invoke")]
    Task InvokeAsync(NeuronId target);
}

internal sealed class RawWorkflowRunner(IGrainFactory grains) : Grain, IRawWorkflowRunner
{
    public Task InvokeAsync(NeuronId target)
        => grains.GetGrain<IRawCapabilityTarget>(target.ToGrainId()).EnterAsync();
}

[Alias("db.test.raw-capability-target")]
internal interface IRawCapabilityTarget : INeuron
{
    [Alias("Enter")]
    Task EnterAsync();
}

internal sealed class RawCapabilityTarget : Neuron, IRawCapabilityTarget
{
    public Task EnterAsync()
    {
        RawCapabilityTargetObservations.RecordEntry(Id);

        return Task.CompletedTask;
    }
}

internal static class RawCapabilityTargetObservations
{
    private static readonly ConcurrentDictionary<NeuronId, int> Entries = new();

    internal static int EntryCount(NeuronId target)
        => Entries.GetValueOrDefault(target);

    internal static void RecordEntry(NeuronId target)
        => Entries.AddOrUpdate(target, 1, static (_, count) => count + 1);
}

[Alias("db.test.kernel-client-entry-probe")]
[ClientEntryPoint]
internal interface IKernelClientEntryProbe : INeuron
{
    [Alias("Enter")]
    Task<int> EnterAsync();
}

internal sealed class KernelClientEntryTarget : Neuron, IKernelClientEntryProbe
{
    private int _entries;

    public Task<int> EnterAsync() => Task.FromResult(++_entries);
}
