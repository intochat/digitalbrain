using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using DigitalBrain.Security;
using DigitalBrain.Testing;
using DigitalBrain.Tasks;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
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
    [Fact(DisplayName = "GroupChat advances one durable Lockstep superstep per Task revision without replaying the participant")]
    public async Task GroupChatAdvancesOneDurableSuperstepPerRevisionWithoutReplayingParticipant()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        AIWorkerLogProvider.Clear();

        var owner = new OwnerId("ai-worker-happy-path");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driverId = NeuronId.For<TaskDriver>(owner, "ai-worker-driver");
        var driver = cluster.Client.GetGrain<ITaskDriver>(driverId.ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var gate = probes.PrepareGate(owner, "complete the supervised run", "terminal answer");

        try
        {
            _ = await task.StartAsync(new(
                    CommandId.New(),
                    new AIWorkerGoal("complete the supervised run"),
                    workerId,
                    new TaskPolicy(1, TimeSpan.Zero, null)))
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
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

            var participantTurn = await task.ReadAsync();

            Assert.Equal(TaskState.Running, participantTurn.State);
            Assert.Equal(1, participantTurn.Revision);
            Assert.Equal(1, gate.EntryCount);

            gate.Release();

            var succeeded = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);
            var result = Assert.IsType<AIWorkerResult>(succeeded.Result);

            Assert.Equal("terminal answer", result.Answer);
            Assert.True(result.OutputWasReadOnly);
            Assert.Equal(2, succeeded.Revision);
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

    [Fact(DisplayName = "completion delegation is minted only after each durable superstep finishes")]
    public async Task CompletionDelegationIsMintedOnlyAfterEachDurableSuperstepFinishes()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-just-in-time-completion");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var gate = probes.PrepareGate(owner, "authorize completion late", "late authorization answer");

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("authorize completion late"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            var whileModelBlocked = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);

            var completedBeforeModel = Assert.Single(
                whileModelBlocked.Delta,
                delivery => delivery.Synapse is CapabilityRequested request
                    && request.Target == workerId);
            Assert.Single(
                whileModelBlocked.Delta,
                delivery => delivery.Synapse is CapabilityCompleted outcome
                    && outcome.Request == completedBeforeModel.SynapseId);

            gate.Release();
            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);

            var completed = await ReadJournalUntilAsync(
                probe,
                workerId,
                journal =>
                {
                    var requests = journal.Delta.Where(delivery =>
                        delivery.Synapse is CapabilityRequested capability
                        && capability.Target == workerId).ToArray();

                    return requests.Length == 3
                        && requests.All(request => journal.Delta.Any(delivery =>
                            delivery.Synapse is CapabilityCompleted outcome
                            && outcome.Request == request.SynapseId));
                });
            var completionRequests = completed.Delta.Where(
                delivery => delivery.Synapse is CapabilityRequested request
                    && request.Target == workerId).ToArray();

            Assert.Equal(3, completionRequests.Length);
            Assert.All(
                completionRequests,
                request => Assert.Single(
                    completed.Delta,
                    delivery => delivery.Synapse is CapabilityCompleted outcome
                        && outcome.Request == request.SynapseId));
        }
        finally
        {
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "an expired recovery reminder persists a fresh run before redispatch and stale output cannot win")]
    public async Task ExpiredRecoveryReminderPersistsFreshRunBeforeRedispatchAndStaleOutputCannotWin()
    {
        const string recoveryReminder = "db.ai.workflow-run";
        var journals = new AIWorkerJournalStorageProvider();
        var clock = new AIWorkerTimeProvider(DateTimeOffset.Parse(
            "2026-07-21T08:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture));
        var (cluster, probes) = await StartWorkerClusterAsync(journals, clock);
        AIWorkerLogProvider.Clear();

        var owner = new OwnerId("ai-worker-reminder-recovery");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        probes.ResetRunnerDispatch(workerId);
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var reminder = cluster.Client.GetGrain<IReminderProbe>(
            NeuronId.For<ReminderProbe>(owner, "reminder-probe").ToGrainId());
        var gate = probes.PrepareGate(
            owner,
            "recover the supervised run",
            "stale answer",
            secondAnswer: "recovered answer");
        AIWorkerWriteGate? replacementWrite = null;

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("recover the supervised run"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var running = await task.ReadAsync();
            Assert.Equal(1, running.Revision);
            Assert.Equal(2, probes.RunnerDispatchEntriesFor(workerId));

            Assert.True(await reminder.ExistsAsync(workerId, recoveryReminder));

            await reminder.ExpediteAsync(workerId, recoveryReminder);
            await Task.Delay(TimeSpan.FromMilliseconds(1500), TestContext.Current.CancellationToken);
            Assert.Equal(1, gate.EntryCount);

            clock.Advance(TimeSpan.FromMinutes(2));
            var writesBeforeReplacement = journals.CompletedWrites(workerId.ToGrainId());
            replacementWrite = journals.BlockNextWrite(workerId.ToGrainId());
            await reminder.ExpediteAsync(workerId, recoveryReminder);

            await replacementWrite.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
            Assert.Equal(2, probes.RunnerDispatchEntriesFor(workerId));
            Assert.Equal(1, gate.EntryCount);

            replacementWrite.Release();
            await WaitUntilAsync(
                () => journals.CompletedWrites(workerId.ToGrainId()) > writesBeforeReplacement,
                "The recovered WorkflowRun replacement was not committed.");
            await WaitUntilAsync(
                () => probes.RunnerDispatchEntriesFor(workerId) == 3,
                "The recovered WorkflowRun was not dispatched after its replacement commit.");
            _ = await probe.ReadWorkerStateAsync(workerId);

            _ = await ReadJournalUntilAsync(
                probe,
                workerId,
                journal => journal.Delta.Count(delivery =>
                    delivery.Synapse is CapabilityRequested request
                    && request.Target == NeuronId.For<IAIWorkerModel>(owner, "worker")) == 2);
            Assert.Equal(1, gate.EntryCount);

            gate.ReleaseFirst();
            await WaitUntilAsync(
                () => AIWorkerLogProvider.Messages.Any(message =>
                    message.Contains("failed before adoption", StringComparison.Ordinal)),
                "The stale workflow runner did not reach its exact active-run fence.");
            await gate.SecondEntry.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            var recoveredRunning = await task.ReadAsync();
            Assert.Equal(TaskState.Running, recoveredRunning.State);
            Assert.Equal(running.ActiveAttempt, recoveredRunning.ActiveAttempt);
            Assert.Equal(running.Revision, recoveredRunning.Revision);
            Assert.Equal("recover the supervised run", gate.InputAt(1)[0].Text);
            Assert.Equal("recover the supervised run", gate.InputAt(2)[0].Text);

            await reminder.ExpediteAsync(workerId, recoveryReminder);
            await Task.Delay(TimeSpan.FromMilliseconds(1500), TestContext.Current.CancellationToken);
            Assert.Equal(2, gate.EntryCount);

            gate.ReleaseSecond();
            var succeeded = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);
            var result = Assert.IsType<AIWorkerResult>(succeeded.Result);
            Assert.Equal("recovered answer", result.Answer);

            var outgoing = await ReadJournalUntilAsync(
                probe,
                workerId,
                journal => journal.Delta.Any(delivery =>
                    delivery.Synapse is CapabilityCompleted));

            Assert.Equal(
                2,
                outgoing.Delta.Count(delivery =>
                    delivery.Synapse is CapabilityRequested request
                    && request.Target == NeuronId.For<IAIWorkerModel>(owner, "worker")));
            Assert.Equal(
                3,
                outgoing.Delta.Count(
                    delivery => delivery.Synapse is CapabilityRequested request
                        && request.Target == workerId));

            await reminder.ExpediteAsync(workerId, recoveryReminder);
            await WaitForReminderStateAsync(
                reminder,
                workerId,
                recoveryReminder,
                exists: false);
        }
        finally
        {
            replacementWrite?.Release();
            gate.Release();
            journals.ClearFailure(workerId.ToGrainId());
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "a failed recovery write neither redispatches nor consumes the expired run")]
    public async Task FailedRecoveryWriteDoesNotDispatchAndTheNextReminderRetries()
    {
        const string recoveryReminder = "db.ai.workflow-run";
        var journals = new AIWorkerJournalStorageProvider();
        var clock = new AIWorkerTimeProvider(DateTimeOffset.Parse(
            "2026-07-21T09:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture));
        var (cluster, probes) = await StartWorkerClusterAsync(journals, clock);
        AIWorkerLogProvider.Clear();

        var owner = new OwnerId("ai-worker-reminder-write-failure");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var workerGrain = workerId.ToGrainId();
        probes.ResetRunnerDispatch(workerId);
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var reminder = cluster.Client.GetGrain<IReminderProbe>(
            NeuronId.For<ReminderProbe>(owner, "reminder-probe").ToGrainId());
        var gate = probes.PrepareGate(
            owner,
            "retry a failed recovery commit",
            "stale answer",
            secondAnswer: "recovered after retry");

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("retry a failed recovery commit"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(await reminder.ExistsAsync(workerId, recoveryReminder));
            Assert.Equal(2, probes.RunnerDispatchEntriesFor(workerId));

            var beforeFailure = await probe.ReadWorkerStateAsync(workerId);
            clock.Advance(TimeSpan.FromMinutes(2));
            journals.FailWriteAfter(
                workerGrain,
                completedWritesBeforeFailure: 0,
                "injected recovery commit failure");
            await reminder.ExpediteAsync(workerId, recoveryReminder);

            await WaitUntilAsync(
                () => journals.FiredFailures(workerGrain) == 1,
                "The recovery commit failure was not injected.");
            Assert.Equal(beforeFailure, await probe.ReadWorkerStateAsync(workerId));
            await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

            var failedJournal = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.Single(
                failedJournal.Delta,
                delivery => delivery.Synapse is CapabilityRequested request
                    && request.Target == NeuronId.For<IAIWorkerModel>(owner, "worker"));
            Assert.Equal(1, gate.EntryCount);
            Assert.Equal(2, probes.RunnerDispatchEntriesFor(workerId));

            journals.ClearFailure(workerGrain);
            await reminder.ExpediteAsync(workerId, recoveryReminder);
            _ = await ReadJournalUntilAsync(
                probe,
                workerId,
                journal => journal.Delta.Count(delivery =>
                    delivery.Synapse is CapabilityRequested request
                    && request.Target == NeuronId.For<IAIWorkerModel>(owner, "worker")) == 2);
            Assert.Equal(3, probes.RunnerDispatchEntriesFor(workerId));

            gate.ReleaseFirst();
            await WaitUntilAsync(
                () => AIWorkerLogProvider.Messages.Any(message =>
                    message.Contains("failed before adoption", StringComparison.Ordinal)),
                "The stale workflow runner did not reach its exact active-run fence.");
            await gate.SecondEntry.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            gate.ReleaseSecond();

            var succeeded = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);
            Assert.Equal("recovered after retry", Assert.IsType<AIWorkerResult>(succeeded.Result).Answer);

            await reminder.ExpediteAsync(workerId, recoveryReminder);
            await WaitForReminderStateAsync(
                reminder,
                workerId,
                recoveryReminder,
                exists: false);
        }
        finally
        {
            gate.Release();
            journals.ClearFailure(workerGrain);
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "cancellation clears the active run and late workflow output cannot overwrite it")]
    public async Task CancellationClearsTheActiveRunAndFencesLateWorkflowOutput()
    {
        const string recoveryReminder = "db.ai.workflow-run";
        var (cluster, probes) = await StartWorkerClusterAsync();
        AIWorkerLogProvider.Clear();

        var owner = new OwnerId("ai-worker-cancel-late-output");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        probes.ResetRunnerDispatch(workerId);
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var reminder = cluster.Client.GetGrain<IReminderProbe>(
            NeuronId.For<ReminderProbe>(owner, "reminder-probe").ToGrainId());
        var gate = probes.PrepareGate(
            owner,
            "cancel the supervised run",
            "output that arrived after cancellation");

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("cancel the supervised run"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var running = await task.ReadAsync();
            Assert.Equal(1, running.Revision);
            Assert.Equal(2, probes.RunnerDispatchEntriesFor(workerId));

            var beforeCancellation = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            var completionRequestsBeforeCancellation = beforeCancellation.Delta.Count(delivery =>
                delivery.Synapse is CapabilityRequested request
                && request.Target == workerId);
            Assert.Equal(1, completionRequestsBeforeCancellation);

            var cancelling = await task.CancelAsync(new(CommandId.New(), running.Revision));
            Assert.Equal(TaskState.Cancelling, cancelling.State);

            var cancelled = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Cancelled);
            Assert.Null(cancelled.ActiveAttempt);
            Assert.Null(cancelled.Result);

            var cancellationJournal = await ReadJournalUntilAsync(
                probe,
                workerId,
                journal => journal.Delta.Any(delivery => delivery.Synapse is AttemptCancelled));
            var acceptedDelivery = Assert.Single(
                cancellationJournal.Delta,
                delivery => delivery.Synapse is AttemptAccepted);
            var cancelledDelivery = Assert.Single(
                cancellationJournal.Delta,
                delivery => delivery.Synapse is AttemptCancelled fact
                    && fact.Task == taskId
                    && fact.Worker == workerId
                    && fact.Attempt == running.ActiveAttempt
                    && fact.Revision == running.Revision);
            Assert.NotNull(acceptedDelivery.CausationId);
            Assert.NotEqual(acceptedDelivery.CausationId, cancelledDelivery.CausationId);
            var incoming = await probe.ReadJournalAsync(workerId, JournalKind.Incoming);
            var continuationRequest = Assert.Single(
                incoming.Delta,
                delivery => delivery.Synapse is CapabilityRequested request
                    && request.Target == workerId
                    && string.Equals(
                        request.Method,
                        nameof(IWorker.Continue),
                        StringComparison.Ordinal));
            Assert.Equal(continuationRequest.SynapseId, cancelledDelivery.CausationId);
            Assert.Equal(
                completionRequestsBeforeCancellation,
                cancellationJournal.Delta.Count(delivery =>
                    delivery.Synapse is CapabilityRequested request
                    && request.Target == workerId));

            var directAfterCancellation = probe.RespondAsync(
                workerId,
                [new ChatMessage(ChatRole.User, "direct after cancellation")]);
            gate.ReleaseFirst();
            await gate.SecondEntry.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            gate.ReleaseSecond();
            var directResponse = await directAfterCancellation;
            Assert.Contains(
                "output that arrived after cancellation",
                directResponse.Text,
                StringComparison.Ordinal);
            Assert.NotEmpty(await probe.ReadDirectStateAsync(workerId));

            await WaitUntilAsync(
                () => AIWorkerLogProvider.Messages.Any(message =>
                    message.Contains("failed before adoption", StringComparison.Ordinal)),
                "The cancelled workflow runner did not reach its late-result fence.");

            var afterLateOutput = await task.ReadAsync();
            Assert.Equal(TaskState.Cancelled, afterLateOutput.State);
            Assert.Equal(cancelled.Revision, afterLateOutput.Revision);
            Assert.Null(afterLateOutput.ActiveAttempt);
            Assert.Null(afterLateOutput.Result);
            var lateJournal = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.Equal(
                completionRequestsBeforeCancellation,
                lateJournal.Delta.Count(delivery =>
                    delivery.Synapse is CapabilityRequested request
                    && request.Target == workerId));

            await reminder.ExpediteAsync(workerId, recoveryReminder);
            await WaitForReminderStateAsync(
                reminder,
                workerId,
                recoveryReminder,
                exists: false);
            Assert.Equal(2, probes.RunnerDispatchEntriesFor(workerId));
        }
        finally
        {
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "a same-owner neuron cannot cancel another Task's active workflow run")]
    public async Task WrongCallerCannotCancelAnActiveWorkflowRun()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-cancel-wrong-caller");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "wrong-caller").ToGrainId());
        var gate = probes.PrepareGate(
            owner,
            "protect cancellation authority",
            "authorized task answer");

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("protect cancellation authority"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            var running = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var before = await probe.ReadWorkerStateAsync(workerId);
            var cursor = new AttemptCursor(
                taskId,
                workerId,
                running.ActiveAttempt!.Value,
                running.Revision);

            _ = await Assert.ThrowsAsync<NeuronAuthorizationException>(
                () => probe.CancelAsync(workerId, cursor));

            Assert.Equal(before, await probe.ReadWorkerStateAsync(workerId));
            Assert.Equal(TaskState.Running, (await task.ReadAsync()).State);
            Assert.Equal(1, gate.EntryCount);

            gate.ReleaseFirst();
            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);
        }
        finally
        {
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "a same-owner neuron cannot continue another Task's adopted checkpoint")]
    public async Task WrongCallerCannotContinueAnAdoptedCheckpoint()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-continue-wrong-caller");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "wrong-caller").ToGrainId());
        var gate = probes.PrepareGate(
            owner,
            "protect checkpoint continuation authority",
            "authorized continuation answer");

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("protect checkpoint continuation authority"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var running = await task.ReadAsync();
            var before = await probe.ReadWorkerStateAsync(workerId);
            var next = new AttemptCursor(
                taskId,
                workerId,
                running.ActiveAttempt!.Value,
                running.Revision + 1);

            _ = await Assert.ThrowsAsync<NeuronAuthorizationException>(
                () => probe.ContinueAsync(workerId, next));

            Assert.Equal(before, await probe.ReadWorkerStateAsync(workerId));
            var after = await task.ReadAsync();
            Assert.Equal(running.State, after.State);
            Assert.Equal(running.Revision, after.Revision);
            Assert.Equal(running.ActiveAttempt, after.ActiveAttempt);
            Assert.Equal(1, gate.EntryCount);

            gate.ReleaseFirst();
            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);
        }
        finally
        {
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "an authorized Task cannot skip the next checkpoint revision")]
    public async Task AuthorizedTaskCannotContinueWithAFutureRevision()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-continue-future-revision");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        probes.ResetRunnerDispatch(workerId);
        var mutation = probes.PrepareContinuationMutation(workerId);
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var gate = probes.PrepareGate(
            owner,
            "reject a future continuation",
            "must not run under a future revision");

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("reject a future continuation"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await mutation.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            var blockedState = await probe.ReadWorkerStateAsync(workerId);

            await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

            var blocked = await task.ReadAsync();
            Assert.Equal(TaskState.Running, blocked.State);
            Assert.Equal(1, blocked.Revision);
            Assert.Equal(1, probes.RunnerDispatchEntriesFor(workerId));
            Assert.Equal(0, gate.EntryCount);
            Assert.Equal(blockedState, await probe.ReadWorkerStateAsync(workerId));
            var outgoing = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.Single(
                outgoing.Delta,
                delivery => delivery.Synapse is AttemptAdvanced advanced
                    && advanced.Revision == 0);
            Assert.DoesNotContain(
                outgoing.Delta,
                delivery => delivery.Synapse is CapabilityRequested request
                    && request.Target == NeuronId.For<IAIWorkerModel>(owner, "worker"));
        }
        finally
        {
            probes.ResetContinuationMutation(workerId);
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "GroupChat copies mutable input and read-only output mapping boundaries")]
    public async Task GroupChatCopiesBothTaskMappingBoundaries()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-mapping-copies");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var gate = probes.PrepareGate(
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
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-active-conflict");
        var firstTaskId = NeuronId.For<ITask>(owner, "first-task");
        var secondTaskId = NeuronId.For<ITask>(owner, "second-task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var first = new TaskTestClient(firstTaskId, driver);
        var second = new TaskTestClient(secondTaskId, driver);
        var gate = probes.PrepareGate(
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
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-direct-exclusion");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var gate = probes.PrepareGate(owner, "supervised run", "supervised answer");

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

            var terminalDirect = await probe.RespondAsync(
                workerId,
                [new ChatMessage(ChatRole.User, "direct after terminal")]);

            Assert.Contains("supervised answer", terminalDirect.Text, StringComparison.Ordinal);
            Assert.Equal(2, gate.EntryCount);
            Assert.NotEmpty(await probe.ReadDirectStateAsync(workerId));
        }
        finally
        {
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "direct Respond remains rejected while a supervised Attempt awaits continuation")]
    public async Task DirectRespondIsRejectedWhileASupervisedAttemptAwaitsContinuation()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-direct-awaiting-continuation");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var mutation = probes.PrepareContinuationMutation(workerId);
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var gate = probes.PrepareGate(
            owner,
            "await continuation",
            "must not enter before continuation");

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("await continuation"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await mutation.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            var workerBefore = await probe.ReadWorkerStateAsync(workerId);
            var directBefore = await probe.ReadDirectStateAsync(workerId);
            var outgoing = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.Single(
                outgoing.Delta,
                delivery => delivery.Synapse is AttemptAdvanced advanced
                    && advanced.Revision == 0);
            Assert.Equal(0, gate.EntryCount);

            var direct = probe.RespondAsync(
                workerId,
                [new ChatMessage(ChatRole.User, "must remain fenced")]);
            var firstCompleted = await Task.WhenAny(direct, gate.Entered)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Same(direct, firstCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => direct);
            Assert.Equal(0, gate.EntryCount);
            Assert.Equal(workerBefore, await probe.ReadWorkerStateAsync(workerId));
            Assert.Equal(directBefore, await probe.ReadDirectStateAsync(workerId));
        }
        finally
        {
            probes.ResetContinuationMutation(workerId);
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "a wrong caller cannot exploit the active Task's duplicate Accept cursor")]
    public async Task WrongCallerCannotReplayTheActiveTasksAccept()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-wrong-duplicate-caller");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var wrongId = NeuronId.For<AIWorkerProbe>(owner, "wrong");
        var wrong = cluster.Client.GetGrain<IAIWorkerProbe>(wrongId.ToGrainId());
        var gate = probes.PrepareGate(owner, "real task run", "real task answer");

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
        var (cluster, probes) = await StartWorkerClusterAsync();
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
        var (cluster, probes) = await StartWorkerClusterAsync();
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
        var (cluster, probes) = await StartWorkerClusterAsync();
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
        var (cluster, probes) = await StartWorkerClusterAsync(journals);
        var owner = new OwnerId("ai-worker-accept-write-rollback");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var gate = probes.PrepareGate(owner, "retry after rollback", "recovered answer");

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

    [Fact(DisplayName = "a failed checkpoint adoption commit cannot leak a cleared ActiveRun or Task fact")]
    public async Task FailedCheckpointAdoptionCommitRollsBackWorkerStateAndTaskFact()
    {
        var journals = new AIWorkerJournalStorageProvider();
        var (cluster, probes) = await StartWorkerClusterAsync(journals);
        AIWorkerLogProvider.Clear();
        var owner = new OwnerId("ai-worker-adoption-write-rollback");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var gate = probes.PrepareGate(owner, "fail adoption", "unadopted answer");

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
            var taskBefore = await task.ReadAsync();
            var outgoingBefore = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            var advancesBefore = outgoingBefore.Delta.Count(
                delivery => delivery.Synapse is AttemptAdvanced);

            journals.FailWriteAfter(
                workerId.ToGrainId(),
                completedWritesBeforeFailure: 3,
                "Expected checkpoint adoption commit failure.");
            gate.Release();

            await WaitUntilAsync(
                () => journals.FiredFailures(workerId.ToGrainId()) == 1,
                "The checkpoint adoption write failure did not fire.");

            Assert.Equal(before, await probe.ReadWorkerStateAsync(workerId));
            var taskAfter = await task.ReadAsync();
            Assert.Equal(TaskState.Running, taskAfter.State);
            Assert.Equal(taskBefore.Revision, taskAfter.Revision);
            Assert.Equal(taskBefore.ActiveAttempt, taskAfter.ActiveAttempt);
            var outgoing = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.Equal(
                advancesBefore,
                outgoing.Delta.Count(delivery => delivery.Synapse is AttemptAdvanced));
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

    [Fact(DisplayName = "GroupChat validates the durable child checkpoint before advancing the Task")]
    public async Task ChildCheckpointReadCompletesBeforeNonterminalAdoption()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-checkpoint-read-order");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var checkpointRead = probes.BlockCheckpointRead(workerId);
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var model = probes.PrepareGate(
            owner,
            "validate the checkpoint child",
            "validated child answer");

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("validate the checkpoint child"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await checkpointRead.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            var blocked = await task.ReadAsync();
            Assert.True(blocked.State is TaskState.Pending or TaskState.Running);
            Assert.Equal(0, blocked.Revision);
            Assert.Equal(0, model.EntryCount);

            checkpointRead.Release();
            await model.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            Assert.Equal(1, (await task.ReadAsync()).Revision);

            model.Release();
            var succeeded = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);
            Assert.Equal(2, succeeded.Revision);
        }
        finally
        {
            checkpointRead.Release();
            probes.ResetCheckpointRead(workerId);
            model.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "checkpoint storage commits before GroupChat adopts terminal output")]
    public async Task CheckpointWriteCompletesBeforeTerminalAdoption()
    {
        var journals = new AIWorkerJournalStorageProvider();
        var (cluster, probes) = await StartWorkerClusterAsync(journals);
        var owner = new OwnerId("ai-worker-checkpoint-order");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var model = probes.PrepareGate(owner, "checkpoint ordering", "ordered answer");
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

    [Fact(DisplayName = "definition drift during the acceptance commit prevents runner dispatch")]
    public async Task DefinitionDriftBeforeDispatchPreventsRunnerEntry()
    {
        var journals = new AIWorkerJournalStorageProvider();
        var (cluster, probes) = await StartWorkerClusterAsync(journals);
        var owner = new OwnerId("ai-worker-definition-dispatch");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var model = probes.PrepareGate(
            owner,
            "fence dispatch drift",
            "must not run");
        var workerWrite = journals.BlockWriteAfter(
            workerId.ToGrainId(),
            completedWritesBeforeBlock: 1);
        probes.ResetRunnerDispatch(workerId);
        probes.ResetDefinitionName(owner);

        try
        {
            var starting = task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("fence dispatch drift"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await workerWrite.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            probes.SetDefinitionName(owner, "drifted-model");
            workerWrite.Release();
            _ = await starting;
            await Task.Delay(
                TimeSpan.FromMilliseconds(750),
                TestContext.Current.CancellationToken);

            Assert.Equal(0, probes.RunnerDispatchEntriesFor(workerId));
            Assert.Equal(0, model.EntryCount);
        }
        finally
        {
            workerWrite.Release();
            probes.ResetDefinitionName(owner);
            model.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "definition drift is rejected before participant authorization and invocation")]
    public async Task DefinitionDriftBeforeAuthorizationPreventsParticipantEntry()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-definition-authorization");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var model = probes.PrepareGate(
            owner,
            "fence authorization drift",
            "must not run");
        var execution = probes.BlockRunnerExecution(workerId);
        probes.ResetDefinitionName(owner);

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("fence authorization drift"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await execution.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            probes.SetDefinitionName(owner, "drifted-model");
            execution.Release();
            await Task.Delay(
                TimeSpan.FromMilliseconds(750),
                TestContext.Current.CancellationToken);

            Assert.Equal(0, model.EntryCount);
            Assert.NotNull(execution.Failure);
        }
        finally
        {
            execution.Release();
            probes.ResetRunnerExecution(workerId);
            probes.ResetDefinitionName(owner);
            model.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "definition drift is rejected before completion authorization and checkpoint adoption")]
    public async Task DefinitionDriftBeforeCompletionPreventsCheckpointAdoption()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-definition-completion");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var model = probes.PrepareGate(
            owner,
            "fence completion drift",
            "must not be adopted");
        probes.ResetDefinitionName(owner);

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("fence completion drift"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await model.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            probes.SetDefinitionName(owner, "drifted-model");
            model.Release();
            await Task.Delay(
                TimeSpan.FromMilliseconds(1250),
                TestContext.Current.CancellationToken);

            var snapshot = await task.ReadAsync();
            Assert.NotEqual(TaskState.Succeeded, snapshot.State);
            var journal = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.DoesNotContain(journal.Delta, delivery => delivery.Synapse is AttemptSucceeded);
        }
        finally
        {
            probes.ResetDefinitionName(owner);
            model.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "an identical Accept replay validates the current definition before returning idempotently")]
    public async Task IdenticalAcceptReplayRejectsCurrentDefinitionDrift()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-definition-accept-replay");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var model = probes.PrepareGate(
            owner,
            "fence identical acceptance replay",
            "must remain blocked");
        var replay = probes.ReplayRpc(workerId, "Accept");
        probes.ResetDefinitionName(owner);

        try
        {
            var starting = task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("fence identical acceptance replay"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await replay.ReplayReady.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            probes.SetDefinitionName(owner, "drifted-model");
            replay.ReleaseReplay();
            _ = await starting.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            await replay.Completed.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Assert.Equal(2, replay.InvocationCount);
            Assert.Equal(1, replay.FailureCount);
            Assert.Contains(
                "incompatible",
                replay.Failure?.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            replay.ReleaseReplay();
            probes.ResetRpc(workerId, "Accept");
            probes.ResetDefinitionName(owner);
            model.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "definition drift is rejected before an expired reminder replaces or redispatches a run")]
    public async Task DefinitionDriftBeforeReminderLeavesTheActiveRunUnchanged()
    {
        const string recoveryReminder = "db.ai.workflow-run";
        var clock = new AIWorkerTimeProvider(DateTimeOffset.Parse(
            "2026-07-23T12:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture));
        var (cluster, probes) = await StartWorkerClusterAsync(clock: clock);
        var owner = new OwnerId("ai-worker-definition-reminder");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var reminder = cluster.Client.GetGrain<IReminderProbe>(
            NeuronId.For<ReminderProbe>(owner, "reminder-probe").ToGrainId());
        var model = probes.PrepareGate(
            owner,
            "fence reminder drift",
            "must remain blocked");
        probes.ResetDefinitionName(owner);

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("fence reminder drift"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await model.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            var before = await probe.ReadWorkerStateAsync(workerId);
            var dispatchesBefore = probes.RunnerDispatchEntriesFor(workerId);

            probes.SetDefinitionName(owner, "drifted-model");
            clock.Advance(TimeSpan.FromMinutes(2));
            await reminder.ExpediteAsync(workerId, recoveryReminder);
            await Task.Delay(
                TimeSpan.FromMilliseconds(1500),
                TestContext.Current.CancellationToken);

            Assert.Equal(before, await probe.ReadWorkerStateAsync(workerId));
            Assert.Equal(
                dispatchesBefore,
                probes.RunnerDispatchEntriesFor(workerId));
            Assert.Equal(1, model.EntryCount);
        }
        finally
        {
            probes.ResetDefinitionName(owner);
            model.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "a silo restart and reminder redispatch adopt the checkpoint exactly once")]
    public async Task SiloRestartAndReminderRedispatchDoNotDuplicateCheckpointAdoption()
    {
        const string recoveryReminder = "db.ai.workflow-run";
        var journals = new AIWorkerJournalStorageProvider();
        var clock = new AIWorkerTimeProvider(DateTimeOffset.Parse(
            "2026-07-23T13:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture));
        var (cluster, probes) = await StartWorkerClusterAsync(journals, clock);
        var owner = new OwnerId("ai-worker-restart-redispatch");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var model = probes.PrepareGate(
            owner,
            "restart the supervised run",
            "stale answer",
            secondAnswer: "recovered answer");

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("restart the supervised run"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
            await model.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            clock.Advance(TimeSpan.FromMinutes(2));
            var crashed = cluster.Silos[0];
            await cluster.KillSiloAsync(crashed)
                .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            var restarted = await cluster.RestartSiloAsync(crashed)
                .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            Assert.NotNull(restarted);
            await cluster.WaitForLivenessToStabilizeAsync(didKill: true)
                .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            await cluster.WaitForClusterManifestToStabilizeAsync(didKill: true)
                .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            Assert.True(restarted.IsActive);

            await cluster.StopClusterClientAsync()
                .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            await cluster.InitializeClientAsync()
                .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            var reconnectedDriver = cluster.Client.GetGrain<ITaskDriver>(
                NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
            var reconnectedTask = new TaskTestClient(taskId, reconnectedDriver);
            var reconnectedProbe = cluster.Client.GetGrain<IAIWorkerProbe>(
                NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
            var reconnectedReminder = cluster.Client.GetGrain<IReminderProbe>(
                NeuronId.For<ReminderProbe>(owner, "reminder-probe").ToGrainId());

            Assert.Equal(TaskState.Running, (await reconnectedTask.ReadAsync()).State);
            await reconnectedReminder.ExpediteAsync(workerId, recoveryReminder);
            await model.SecondEntry.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

            model.Release();
            var succeeded = await ReadUntilAsync(
                reconnectedTask,
                snapshot => snapshot.State == TaskState.Succeeded);
            Assert.Equal(
                "recovered answer",
                Assert.IsType<AIWorkerResult>(succeeded.Result).Answer);

            var adopted = await reconnectedProbe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.Single(adopted.Delta, delivery => delivery.Synapse is AttemptSucceeded);

            await reconnectedReminder.ExpediteAsync(workerId, recoveryReminder);
            await WaitForReminderStateAsync(
                reconnectedReminder,
                workerId,
                recoveryReminder,
                exists: false);
            var afterDuplicateReminder = await reconnectedProbe.ReadJournalAsync(
                workerId,
                JournalKind.Outgoing);
            Assert.Single(
                afterDuplicateReminder.Delta,
                delivery => delivery.Synapse is AttemptSucceeded);
        }
        finally
        {
            model.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "definition drift is the first failure at the real Task continuation boundary")]
    public async Task DefinitionDriftPrecedesContinuationStateChecks()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-definition-continuation");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var model = probes.PrepareGate(
            owner,
            "fence continuation drift",
            "must not run");
        var continuation = probes.BlockContinuation(workerId);
        probes.ResetDefinitionName(owner);

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("fence continuation drift"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await continuation.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            probes.SetDefinitionName(owner, "drifted-model");
            continuation.Release();
            await continuation.Completed.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Assert.NotNull(continuation.Failure);
            Assert.Contains(
                "incompatible",
                continuation.Failure.ToString(),
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, model.EntryCount);
        }
        finally
        {
            continuation.Release();
            probes.ResetContinuation(workerId);
            probes.ResetDefinitionName(owner);
            model.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "cancellation commits its durable fence before signaling and terminates the owned runner")]
    public async Task CancellationCommitsFenceBeforeSignalAndTerminatesOwnedRunner()
    {
        var journals = new AIWorkerJournalStorageProvider();
        var (cluster, probes) = await StartWorkerClusterAsync(journals);
        var owner = new OwnerId("ai-worker-cancellation-propagation");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var model = probes.PrepareGate(
            owner,
            "cancel the owned runner",
            "must remain blocked");
        var runner = probes.PrepareRunnerLifetime(workerId);
        var cancellation = probes.BlockRunnerCancellation(workerId);
        AIWorkerLogProvider.Clear();

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("cancel the owned runner"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
            await model.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            var running = await task.ReadAsync();
            await runner.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            var writesBeforeCancellation = journals.CompletedWrites(workerId.ToGrainId());

            var cancelling = task.CancelAsync(new(CommandId.New(), running.Revision));
            await cancellation.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Assert.True(
                journals.CompletedWrites(workerId.ToGrainId()) > writesBeforeCancellation,
                "The runner cancellation signal arrived before the durable worker fence committed.");

            cancellation.Release();
            _ = await cancelling;
            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Cancelled);
            await WaitUntilAsync(
                () => AIWorkerLogProvider.Messages.Any(message =>
                    message.Contains("CanceledException", StringComparison.Ordinal)),
                $"The owned workflow execution did not terminate through its cancellation token.{Environment.NewLine}"
                + string.Join(Environment.NewLine, AIWorkerLogProvider.Messages));
            Assert.Equal(1, cancellation.EntryCount);
            Assert.Equal(runner.RunId, cancellation.RunId);
            Assert.Equal(1, model.EntryCount);
        }
        finally
        {
            cancellation.Release();
            model.Release();
            probes.ResetRunnerCancellation(workerId);
            probes.ResetRunnerLifetime(workerId);
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "a failed runner cancellation signal cannot reopen the durable cancellation fence")]
    public async Task FailedRunnerCancellationSignalLeavesTheAttemptCancelled()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-cancellation-signal-failure");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var model = probes.PrepareGate(
            owner,
            "fail the runner signal",
            "must remain blocked");
        var cancellation = probes.FailRunnerCancellation(
            workerId,
            "injected runner cancellation failure");

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("fail the runner signal"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            _ = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
            await model.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            var running = await task.ReadAsync();

            _ = await task.CancelAsync(new(CommandId.New(), running.Revision));

            await cancellation.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            var cancelled = await ReadUntilAsync(
                task,
                snapshot => snapshot.State == TaskState.Cancelled);

            Assert.Null(cancelled.ActiveAttempt);
            Assert.Equal(1, cancellation.EntryCount);
        }
        finally
        {
            model.Release();
            probes.ResetRunnerCancellation(workerId);
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "the runner send wait cancels a pending ValueTask without waiting for its late fault")]
    public async Task RunnerSendWaitCancelsPendingValueTaskAndDetachesFromItsLateFault()
    {
        var runner = typeof(AIModule).Assembly.GetType(
            "DigitalBrain.AI.WorkflowRunner",
            throwOnError: true)!;
        var wait = Assert.Single(
            runner.GetMethods(
                System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.NonPublic),
            method => method.Name == "AwaitSendAsync");
        var pending = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var waiting = Assert.IsType<Task<bool>>(
            wait.Invoke(
                null,
                [new ValueTask<bool>(pending.Task), cancellation.Token]),
            exactMatch: false);

        await cancellation.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => waiting.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken));
        pending.TrySetException(new InvalidOperationException("late send failure"));
        await Task.Delay(
            TimeSpan.FromMilliseconds(100),
            TestContext.Current.CancellationToken);
        Assert.True(pending.Task.IsFaulted);
    }

    [Fact(DisplayName = "runner cancellation initiates ordered real MAF cleanup without waiting for blocked executor disposal")]
    public async Task RunnerCancellationInitiatesRealMafCleanupAndDetachesFromBlockedDisposal()
    {
        var executor = new BlockingDisposalExecutor();
        var workflow = new WorkflowBuilder(executor).Build();
        StreamingRun? execution = null;

        try
        {
            execution = await InProcessExecution.Lockstep.RunStreamingAsync(
                workflow,
                "input",
                cancellationToken: TestContext.Current.CancellationToken);
            var runner = typeof(AIModule).Assembly.GetType(
                "DigitalBrain.AI.WorkflowRunner",
                throwOnError: true)!;
            var cleanup = Assert.Single(
                runner.GetMethods(
                    System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.NonPublic),
                method => method.Name == "AwaitCleanupAsync");
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            var waiting = Assert.IsType<Task>(
                cleanup.Invoke(null, [execution, cancellation.Token]),
                exactMatch: false);

            await executor.DisposalEntered.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => waiting.WaitAsync(
                    TimeSpan.FromSeconds(2),
                    TestContext.Current.CancellationToken));
            Assert.False(executor.DisposalCompleted.IsCompleted);

            executor.FailDisposal(new InvalidOperationException("late executor disposal failure"));
            var completed = await Task.WhenAny(
                executor.DisposalCompleted,
                Task.Delay(
                    TimeSpan.FromSeconds(2),
                    TestContext.Current.CancellationToken));
            Assert.Same(executor.DisposalCompleted, completed);
            Assert.True(executor.DisposalCompleted.IsFaulted);
        }
        finally
        {
            executor.ReleaseDisposal();

            if (execution is not null)
            {
                await execution.DisposeAsync();
            }
        }
    }

    [Fact(DisplayName = "cancellation terminates runner cleanup while participant authorization remains blocked")]
    public async Task CancellationTerminatesRunnerWhileParticipantAuthorizationRpcRemainsBlocked()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        AIWorkerLogProvider.Clear();
        var owner = new OwnerId("ai-worker-cancel-participant-authorization");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var model = probes.PrepareGate(
            owner,
            "cancel blocked participant authorization",
            "must not run");
        var authorization = probes.BlockRpc(
            workerId,
            "AuthorizeParticipantAsync");

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("cancel blocked participant authorization"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await authorization.InvocationCompleted.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            var running = await task.ReadAsync();

            _ = await task.CancelAsync(new(CommandId.New(), running.Revision));
            _ = await ReadUntilAsync(
                task,
                snapshot => snapshot.State == TaskState.Cancelled);
            await WaitUntilAsync(
                () => AIWorkerLogProvider.Messages.Any(message =>
                    message.Contains("CanceledException", StringComparison.Ordinal)),
                "The runner stayed attached to the blocked participant authorization RPC.");

            Assert.False(authorization.Completed.IsCompleted);
            Assert.Equal(0, model.EntryCount);
            var outgoing = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.DoesNotContain(
                outgoing.Delta,
                delivery => delivery.Synapse is AttemptSucceeded);

            authorization.Release();
            await authorization.Completed.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            Assert.Null(authorization.Failure);
        }
        finally
        {
            authorization.Release();
            probes.ResetRpc(workerId, "AuthorizeParticipantAsync");
            model.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "cancellation terminates runner cleanup while completion adoption remains blocked")]
    public async Task CancellationTerminatesRunnerWhileCompletionRpcRemainsBlocked()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        AIWorkerLogProvider.Clear();
        var owner = new OwnerId("ai-worker-cancel-completion-rpc");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var model = probes.PrepareGate(
            owner,
            "cancel blocked completion",
            "must not be adopted");
        var completionAuthorization = probes.BlockRpc(
            workerId,
            "AuthorizeCompletionAsync");
        var completion = probes.ObserveRpc(workerId, "CompleteAsync");
        model.Release();

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("cancel blocked completion"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await completionAuthorization.InvocationCompleted.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            var running = await task.ReadAsync();

            _ = await task.CancelAsync(new(CommandId.New(), running.Revision));
            _ = await ReadUntilAsync(
                task,
                snapshot => snapshot.State == TaskState.Cancelled);
            await WaitUntilAsync(
                () => AIWorkerLogProvider.Messages.Any(message =>
                    message.Contains("CanceledException", StringComparison.Ordinal)),
                $"The runner stayed attached to the blocked completion RPC.{Environment.NewLine}"
                + string.Join(Environment.NewLine, AIWorkerLogProvider.Messages));

            Assert.False(completionAuthorization.Completed.IsCompleted);
            Assert.Equal(0, completion.EntryCount);
            var outgoing = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.DoesNotContain(
                outgoing.Delta,
                delivery => delivery.Synapse is AttemptSucceeded);

            completionAuthorization.Release();
            await completionAuthorization.Completed.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            Assert.Null(completionAuthorization.Failure);
            Assert.Equal(0, completion.EntryCount);
            Assert.Equal(
                TaskState.Cancelled,
                (await task.ReadAsync()).State);
        }
        finally
        {
            completionAuthorization.Release();
            probes.ResetRpc(workerId, "AuthorizeCompletionAsync");
            probes.ResetRpc(workerId, "CompleteAsync");
            model.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "a cancellation commit failure neither signals nor consumes reply authority and reminder retry completes")]
    public async Task FailedCancellationCommitPreservesFenceFactAndRetryAuthority()
    {
        const string dispatchReminder = "tasks.dispatch";
        var journals = new AIWorkerJournalStorageProvider();
        var (cluster, probes) = await StartWorkerClusterAsync(journals);
        var owner = new OwnerId("ai-worker-cancel-commit-failure");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var reminder = cluster.Client.GetGrain<IReminderProbe>(
            NeuronId.For<ReminderProbe>(owner, "reminder-probe").ToGrainId());
        var model = probes.PrepareGate(
            owner,
            "retry cancellation commit",
            "must remain blocked");
        var cancellation = probes.BlockRunnerCancellation(workerId);
        var workerGrain = workerId.ToGrainId();

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("retry cancellation commit"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await model.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            var running = await task.ReadAsync();
            var workerBefore = await probe.ReadWorkerStateAsync(workerId);
            var outgoingBefore = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            journals.FailWriteAfter(
                workerGrain,
                completedWritesBeforeFailure: 0,
                "injected cancellation commit failure");

            _ = await task.CancelAsync(new(CommandId.New(), running.Revision));
            await WaitUntilAsync(
                () => journals.FiredFailures(workerGrain) == 1,
                "The cancellation commit failure was not injected.");

            Assert.Equal(0, cancellation.EntryCount);
            Assert.Equal(workerBefore, await probe.ReadWorkerStateAsync(workerId));
            var afterFailure = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.Equal(outgoingBefore.ResumeSequence, afterFailure.ResumeSequence);
            Assert.Equal(
                outgoingBefore.Delta.Select(delivery => delivery.SynapseId),
                afterFailure.Delta.Select(delivery => delivery.SynapseId));
            Assert.DoesNotContain(
                afterFailure.Delta,
                delivery => delivery.Synapse is AttemptCancelled);
            Assert.Equal(TaskState.Cancelling, (await task.ReadAsync()).State);

            journals.ClearFailure(workerGrain);
            await reminder.ExpediteAsync(taskId, dispatchReminder);
            await cancellation.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            cancellation.Release();
            _ = await ReadUntilAsync(
                task,
                snapshot => snapshot.State == TaskState.Cancelled);
            var afterRetry = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.Single(
                afterRetry.Delta,
                delivery => delivery.Synapse is AttemptCancelled);
            Assert.Equal(1, cancellation.EntryCount);
        }
        finally
        {
            journals.ClearFailure(workerGrain);
            cancellation.Release();
            probes.ResetRunnerCancellation(workerId);
            model.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "a failed terminal cancellation commit preserves a coherent durable fence for one atomic retry")]
    public async Task FailedTerminalCancellationCommitPreservesCoherentDurableFenceForRetry()
    {
        const string dispatchReminder = "tasks.dispatch";
        const string recoveryReminder = "db.ai.workflow-run";
        var journals = new AIWorkerJournalStorageProvider();
        var clock = new AIWorkerTimeProvider(DateTimeOffset.Parse(
            "2026-07-23T15:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture));
        var (cluster, probes) = await StartWorkerClusterAsync(journals, clock);
        var owner = new OwnerId("ai-worker-cancel-terminal-commit-failure");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var reminder = cluster.Client.GetGrain<IReminderProbe>(
            NeuronId.For<ReminderProbe>(owner, "reminder-probe").ToGrainId());
        var model = probes.PrepareGate(
            owner,
            "fail terminal cancellation commit",
            "must remain blocked");
        var cancellation = probes.BlockRunnerCancellation(workerId);
        var workerCancellation = probes.BlockRpc(
            workerId,
            nameof(IWorker.Cancel));
        var workerGrain = workerId.ToGrainId();

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("fail terminal cancellation commit"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await model.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            var running = await task.ReadAsync();
            var before = await probe.ReadDurableWorkerAsync(workerId);
            Assert.Equal(0, before.OutboxCount);
            Assert.Equal(1, before.CapturedCauseCount);
            journals.FailWriteAfter(
                workerGrain,
                completedWritesBeforeFailure: 2,
                "injected terminal cancellation commit failure");

            var cancelling = task.CancelAsync(new(CommandId.New(), running.Revision));
            await cancellation.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            cancellation.Release();
            await WaitUntilAsync(
                () => journals.FiredFailures(workerGrain) == 1,
                "The terminal cancellation commit failure was not injected.");
            var fencedRunId = cancellation.RunId;

            var inMemory = await probe.ReadDurableWorkerAsync(workerId);
            Assert.NotEqual(before.WorkerState, inMemory.WorkerState);
            Assert.Equal(0, inMemory.OutboxCount);
            Assert.Equal(1, inMemory.CapturedCauseCount);
            var afterFailure = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.DoesNotContain(
                afterFailure.Delta,
                delivery => delivery.Synapse is AttemptCancelled);

            await probe.DeactivateWorkerAsync(workerId);
            var reloaded = await probe.ReadDurableWorkerAsync(workerId);
            Assert.NotEqual(inMemory.ActivationId, reloaded.ActivationId);
            Assert.Equal(inMemory.WorkerState, reloaded.WorkerState);
            Assert.Equal(0, reloaded.OutboxCount);
            Assert.Equal(1, reloaded.CapturedCauseCount);
            var direct = probe.RespondAsync(
                workerId,
                [new ChatMessage(ChatRole.User, "must remain fenced")]);
            var directCompleted = await Task.WhenAny(
                direct,
                Task.Delay(
                    TimeSpan.FromSeconds(2),
                    TestContext.Current.CancellationToken));
            Assert.Same(direct, directCompleted);
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => direct);

            var dispatchesBeforeReminder = probes.RunnerDispatchEntriesFor(workerId);
            clock.Advance(TimeSpan.FromMinutes(2));
            await reminder.ExpediteAsync(workerId, recoveryReminder);
            await WaitUntilAsync(
                () => cancellation.EntryCount == 2,
                "The cancellation-pending reminder did not re-signal the exact fenced run.");
            var afterReminder = await probe.ReadDurableWorkerAsync(workerId);
            Assert.Equal(fencedRunId, cancellation.RunId);
            Assert.Equal(reloaded.WorkerState, afterReminder.WorkerState);
            Assert.Equal(
                dispatchesBeforeReminder,
                probes.RunnerDispatchEntriesFor(workerId));

            workerCancellation.Release();
            _ = await cancelling.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            probes.ResetRpc(workerId, nameof(IWorker.Cancel));
            Assert.Equal(TaskState.Cancelling, (await task.ReadAsync()).State);

            journals.ClearFailure(workerGrain);
            await reminder.ExpediteAsync(taskId, dispatchReminder);
            _ = await ReadUntilAsync(
                task,
                snapshot => snapshot.State == TaskState.Cancelled);
            var afterRetry = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.Single(
                afterRetry.Delta,
                delivery => delivery.Synapse is AttemptCancelled);
            Assert.Equal(3, cancellation.EntryCount);
            Assert.Equal(fencedRunId, cancellation.RunId);

            AIWorkerDurableObservation final = null!;

            for (var attempt = 0; attempt < 100; attempt++)
            {
                final = await probe.ReadDurableWorkerAsync(workerId);

                if (final.OutboxCount == 0)
                {
                    break;
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(20),
                    TestContext.Current.CancellationToken);
            }

            Assert.NotEqual(reloaded.WorkerState, final.WorkerState);
            Assert.Equal(0, final.OutboxCount);
            Assert.Equal(0, final.CapturedCauseCount);
        }
        finally
        {
            journals.ClearFailure(workerGrain);
            workerCancellation.Release();
            probes.ResetRpc(workerId, nameof(IWorker.Cancel));
            cancellation.Release();
            probes.ResetRunnerCancellation(workerId);
            model.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "a repeated committed cancellation no-ops without duplicate signal fact or state mutation")]
    public async Task RepeatedCommittedCancellationIsAnExactNoOp()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-cancel-idempotent-retry");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var model = probes.PrepareGate(
            owner,
            "repeat committed cancellation",
            "must remain blocked");
        var cancellation = probes.BlockRunnerCancellation(workerId);
        var workerCalls = probes.ReplayRpc(workerId, "Cancel");

        try
        {
            _ = await task.StartAsync(new(
                CommandId.New(),
                new AIWorkerGoal("repeat committed cancellation"),
                workerId,
                new TaskPolicy(1, TimeSpan.Zero, null)));
            await model.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            var running = await task.ReadAsync();
            var cancelling = task.CancelAsync(new(CommandId.New(), running.Revision));
            await cancellation.Entered.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            cancellation.Release();
            await workerCalls.ReplayReady.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            Assert.Equal(1, workerCalls.EntryCount);
            Assert.True(
                workerCalls.FailureCount == 0,
                workerCalls.Failure?.ToString());
            var committedState = await probe.ReadWorkerStateAsync(workerId);
            var committedJournal = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.Single(
                committedJournal.Delta,
                delivery => delivery.Synapse is AttemptCancelled);

            workerCalls.ReleaseReplay();
            _ = await cancelling;
            await workerCalls.Completed.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Assert.Equal(2, workerCalls.InvocationCount);
            Assert.True(
                workerCalls.FailureCount == 0,
                workerCalls.Failure?.ToString());
            Assert.Equal(1, cancellation.EntryCount);
            Assert.Equal(committedState, await probe.ReadWorkerStateAsync(workerId));
            var afterRetry = await probe.ReadJournalAsync(workerId, JournalKind.Outgoing);
            Assert.Single(
                afterRetry.Delta,
                delivery => delivery.Synapse is AttemptCancelled);

            _ = await ReadUntilAsync(
                task,
                snapshot => snapshot.State == TaskState.Cancelled);
        }
        finally
        {
            cancellation.Release();
            workerCalls.ReleaseReplay();
            probes.ResetRpc(workerId, "Cancel");
            probes.ResetRunnerCancellation(workerId);
            model.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "a failed checkpoint write leaves no ghost and its retry is the sole readable child")]
    public async Task FailedCheckpointWriteRollsBackAllCollectionsBeforeRetry()
    {
        var journals = new AIWorkerJournalStorageProvider();
        var (cluster, probes) = await StartWorkerClusterAsync(journals);
        var owner = new OwnerId("ai-worker-checkpoint-rollback");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var attempt = new AttemptId(Guid.NewGuid());
        var checkpointGrain = CheckpointGrain(taskId, workerId, attempt);
        var harness = cluster.Client.GetGrain<IAIWorkerCheckpointHarness>(
            NeuronId.For<AIWorkerCheckpointHarness>(owner, "checkpoint-harness").ToGrainId());

        try
        {
            journals.FailWriteAfter(
                checkpointGrain,
                completedWritesBeforeFailure: 0,
                "injected checkpoint write failure");

            var observed = await harness.FailThenRetryAsync(taskId, workerId, attempt);

            Assert.True(observed.FailureObserved);
            Assert.Equal(0, observed.ChildrenAfterFailure);
            Assert.Equal(1, observed.ChildrenAfterRetry);
            Assert.True(observed.RetryPayloadReadable);
        }
        finally
        {
            journals.ClearFailure(checkpointGrain);
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "checkpoint protection rejects the same lineage under a different definition fingerprint")]
    public async Task CheckpointProtectionBindsTheDefinitionFingerprint()
    {
        var (cluster, probes) = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-checkpoint-definition-binding");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var attempt = new AttemptId(Guid.NewGuid());
        var harness = cluster.Client.GetGrain<IAIWorkerCheckpointHarness>(
            NeuronId.For<AIWorkerCheckpointHarness>(owner, "checkpoint-harness").ToGrainId());

        try
        {
            Assert.True(await harness.CrossDefinitionReadFailsAsync(
                taskId,
                workerId,
                attempt,
                "definition-a",
                "definition-b"));
        }
        finally
        {
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
        var probe = SimulationCluster.Grains.GetGrain<IRawCapabilityTargetControl>(target.ToGrainId());

        Assert.Equal(0, await probe.EntryCountAsync());
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

    private static async Task WaitForReminderStateAsync(
        IReminderProbe reminder,
        NeuronId target,
        string reminderName,
        bool exists)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (await reminder.ExistsAsync(target, reminderName) != exists)
        {
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

    private static async Task<(InProcessTestCluster Cluster, AIWorkerTestProbes Probes)> StartWorkerClusterAsync(
        IJournalStorageProvider? journalStorage = null,
        TimeProvider? clock = null)
    {
        var probes = new AIWorkerTestProbes();
        var builder = new InProcessTestClusterBuilder(1);

        builder.ConfigureSilo((_, silo) =>
        {
            silo.Configuration[DurablePayloadProtector.ConfigurationKey] =
                Convert.ToBase64String(new byte[32]);
            silo.AddDigitalBrain("ai-worker-contracts");
            AIModule.Configure(silo);
            silo.UseInMemoryReminderService();
            silo.Services.AddSingleton(probes);
            silo.Services.AddSingleton<IJournalStorageProvider>(
                journalStorage ?? new VolatileJournalStorageProvider());
            silo.Services.AddSingleton<ILoggerProvider>(AIWorkerLogProvider.Instance);
            silo.AddIncomingGrainCallFilter<AIWorkerRunnerDispatchFilter>();
            silo.AddOutgoingGrainCallFilter<AIWorkerRunnerOutgoingFilter>();

            if (clock is not null)
            {
                silo.Services.AddKeyedSingleton(
                    "ai.group-chat.clock",
                    clock);
            }
        });
        builder.ConfigureClient(client =>
        {
            client.Services.AddSerializer(serializer => serializer.AddJsonSerializer(
                type => type == typeof(ChatMessage) || type == typeof(ChatResponse)));
        });

        var cluster = builder.Build();
        await cluster.DeployAsync();

        return (cluster, probes);
    }
}

internal sealed class AIWorkerRunnerDispatchFilter(AIWorkerTestProbes probes) : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (probes.TryMutateContinuation(
                context.TargetId,
                context.InterfaceMethod?.Name,
                context.Request.GetArgumentCount() == 1
                    ? context.Request.GetArgument(0)
                    : null,
                out var mutated))
        {
            context.Request.SetArgument(0, mutated);
        }

        if (probes.TryGetCheckpointRead(
                context.TargetId,
                context.InterfaceMethod?.Name,
                out var checkpointRead))
        {
            await checkpointRead.BlockAsync();
        }

        if (probes.TryGetContinuation(
                context.TargetId,
                context.InterfaceMethod?.Name,
                out var continuation))
        {
            await continuation.EnterAsync();

            try
            {
                await context.Invoke();
            }
            catch (Exception failure)
            {
                continuation.RecordFailure(failure);
                throw;
            }
            finally
            {
                continuation.Complete();
            }

            return;
        }

        var isRunner = string.Equals(
            context.TargetId.Type.ToString(),
            "ai-workflow-runner",
            StringComparison.Ordinal);

        if (isRunner
            && string.Equals(
                context.InterfaceMethod?.Name,
                "CancelAsync",
                StringComparison.Ordinal)
            && probes.TryGetRunnerCancellation(context.TargetId, out var cancellation)
            && context.Request.GetArgument(0) is Guid runId)
        {
            await cancellation.EnterAsync(runId);
        }

        if (isRunner
            && string.Equals(
                context.InterfaceMethod?.Name,
                "ExecuteAsync",
                StringComparison.Ordinal))
        {
            probes.RecordRunnerDispatch(context.TargetId);
            var lifetime = probes.TryEnterRunnerLifetime(
                context.TargetId,
                context.Request.GetArgument(0));
            var execution = probes.TryGetRunnerExecution(context.TargetId);

            try
            {
                if (execution is not null)
                {
                    await execution.EnterAsync();
                }

                await context.Invoke();
            }
            catch (Exception failure)
            {
                execution?.RecordFailure(failure);
                throw;
            }
            finally
            {
                execution?.Complete();
                lifetime?.Complete();
            }

            return;
        }

        await context.Invoke();
    }
}

internal sealed class AIWorkerRunnerOutgoingFilter(AIWorkerTestProbes probes) : IOutgoingGrainCallFilter
{
    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        if (!probes.TryGetRpc(
                context.TargetId,
                context.InterfaceMethod.Name,
                out var observation))
        {
            await context.Invoke();
            return;
        }

        observation.Enter();
        var replay = observation.TryBeginReplay()
            ? RequireReplay(context)
            : null;

        try
        {
            await InvokeOnceAsync(context, observation);

            if (replay is not null)
            {
                observation.ReadyReplay();
                await observation.WaitForReplayReleaseAsync();
                await replay(context.Grain);
            }
        }
        finally
        {
            await observation.WaitForResponseReleaseAsync();
            observation.CompleteResponse();
        }
    }

    private static async Task InvokeOnceAsync(
        IOutgoingGrainCallContext context,
        AIWorkerRpcObservation observation)
    {
        try
        {
            await context.Invoke();
            observation.CompleteInvocation(null);
        }
        catch (Exception failure)
        {
            observation.CompleteInvocation(failure);
            throw;
        }
    }

    private static Func<object, Task> RequireReplay(IOutgoingGrainCallContext context)
    {
        if (context.Request.GetArgumentCount() != 1)
        {
            throw new InvalidOperationException(
                "RPC replay requires exactly one worker argument. "
                + $"Grain: {context.Grain?.GetType().FullName ?? "<null>"}, "
                + $"arguments: {context.Request.GetArgumentCount()}.");
        }

        var argument = context.Request.GetArgument(0);

        return (context.InterfaceMethod.Name, argument) switch
        {
            (nameof(IWorker.Accept), AttemptRequest request) =>
                grain => ((IWorker)grain).Accept(request),
            (nameof(IWorker.Cancel), AttemptCursor cursor) =>
                grain => ((IWorker)grain).Cancel(cursor),
            _ => throw new InvalidOperationException(
                $"RPC replay does not support '{context.InterfaceMethod.Name}' "
                + $"with argument '{argument?.GetType().FullName ?? "<null>"}'."),
        };
    }
}

internal sealed class AIWorkerTestProbes
{
    private const string CheckpointSeparator = "/workflow-checkpoint/";
    private const string RunnerSeparator = "/workflow-run/";

    private readonly ConcurrentDictionary<(GrainId Target, string Method), AIWorkerRpcObservation> _rpc = new();
    private readonly ConcurrentDictionary<GrainId, AIWorkerContinuationMutation> _mutations = new();
    private readonly ConcurrentDictionary<GrainId, AIWorkerContinuationGate> _continuations = new();
    private readonly ConcurrentDictionary<string, AIWorkerCheckpointReadGate> _checkpointReads =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _runnerDispatches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AIWorkerRunnerExecution> _runnerExecutions =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AIWorkerRunnerLifetime> _runnerLifetimes =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AIWorkerRunnerCancellation> _runnerCancellations =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<OwnerId, AIWorkerGate> _gates = new();
    private readonly ConcurrentDictionary<OwnerId, string> _definitionNames = new();

    internal AIWorkerGate PrepareGate(
        OwnerId owner,
        string prompt,
        string answer,
        bool mutateSourceDuringEnumeration = false,
        bool requirePromptMatch = true,
        string? secondAnswer = null)
    {
        var gate = new AIWorkerGate(
            prompt,
            answer,
            mutateSourceDuringEnumeration,
            requirePromptMatch,
            secondAnswer);
        _gates[owner] = gate;
        return gate;
    }

    internal AIWorkerGate GateFor(OwnerId owner)
        => _gates.TryGetValue(owner, out var gate)
            ? gate
            : throw new InvalidOperationException($"No AI worker gate is prepared for owner '{owner}'.");

    internal void ResetGate(OwnerId owner) => _gates.TryRemove(owner, out _);

    internal string DefinitionNameFor(OwnerId owner, string fallback)
        => _definitionNames.TryGetValue(owner, out var name) ? name : fallback;

    internal void SetDefinitionName(OwnerId owner, string name) => _definitionNames[owner] = name;

    internal void ResetDefinitionName(OwnerId owner) => _definitionNames.TryRemove(owner, out _);

    internal AIWorkerRpcObservation BlockRpc(NeuronId target, string method)
        => AddRpc(target, method, block: true);

    internal AIWorkerRpcObservation ObserveRpc(NeuronId target, string method)
        => AddRpc(target, method, block: false);

    internal AIWorkerRpcObservation ReplayRpc(NeuronId target, string method)
        => AddRpc(target, method, block: false, replay: true);

    internal bool TryGetRpc(
        GrainId target,
        string? method,
        out AIWorkerRpcObservation observation)
    {
        if (method is not null
            && _rpc.TryGetValue((target, method), out var found))
        {
            observation = found;
            return true;
        }

        observation = null!;
        return false;
    }

    internal void ResetRpc(NeuronId target, string method)
        => _rpc.TryRemove((target.ToGrainId(), method), out _);

    internal AIWorkerContinuationMutation PrepareContinuationMutation(NeuronId worker)
    {
        var mutation = new AIWorkerContinuationMutation();

        if (!_mutations.TryAdd(worker.ToGrainId(), mutation))
        {
            throw new InvalidOperationException($"Worker '{worker}' already has a continuation mutation.");
        }

        return mutation;
    }

    internal bool TryMutateContinuation(
        GrainId target,
        string? method,
        object? argument,
        out AttemptCursor mutated)
    {
        if (string.Equals(method, nameof(IWorker.Continue), StringComparison.Ordinal)
            && argument is AttemptCursor cursor
            && _mutations.TryGetValue(target, out var mutation))
        {
            mutated = mutation.Mutate(cursor);
            return true;
        }

        mutated = null!;
        return false;
    }

    internal void ResetContinuationMutation(NeuronId worker)
        => _mutations.TryRemove(worker.ToGrainId(), out _);

    internal AIWorkerContinuationGate BlockContinuation(NeuronId worker)
    {
        var gate = new AIWorkerContinuationGate();

        if (!_continuations.TryAdd(worker.ToGrainId(), gate))
        {
            throw new InvalidOperationException($"Worker '{worker}' already has a continuation gate.");
        }

        return gate;
    }

    internal bool TryGetContinuation(
        GrainId target,
        string? method,
        out AIWorkerContinuationGate gate)
    {
        if (string.Equals(method, nameof(IWorker.Continue), StringComparison.Ordinal)
            && _continuations.TryGetValue(target, out var found))
        {
            gate = found;
            return true;
        }

        gate = null!;
        return false;
    }

    internal void ResetContinuation(NeuronId worker)
        => _continuations.TryRemove(worker.ToGrainId(), out _);

    internal AIWorkerCheckpointReadGate BlockCheckpointRead(NeuronId worker)
    {
        var gate = new AIWorkerCheckpointReadGate();

        if (!_checkpointReads.TryAdd(worker.GrainKey, gate))
        {
            throw new InvalidOperationException($"Worker '{worker}' already has a checkpoint read gate.");
        }

        return gate;
    }

    internal bool TryGetCheckpointRead(
        GrainId target,
        string? method,
        out AIWorkerCheckpointReadGate gate)
    {
        var key = target.Key.ToString();
        var separator = key.IndexOf(CheckpointSeparator, StringComparison.Ordinal);

        if (string.Equals(method, "ReadAsync", StringComparison.Ordinal)
            && separator > 0
            && _checkpointReads.TryGetValue(key[..separator], out var found))
        {
            gate = found;
            return true;
        }

        gate = null!;
        return false;
    }

    internal void ResetCheckpointRead(NeuronId worker)
        => _checkpointReads.TryRemove(worker.GrainKey, out _);

    internal int RunnerDispatchEntriesFor(NeuronId worker)
        => _runnerDispatches.GetValueOrDefault(worker.GrainKey);

    internal void RecordRunnerDispatch(GrainId runner)
    {
        var key = runner.Key.ToString();
        var separator = key.IndexOf(RunnerSeparator, StringComparison.Ordinal);

        if (separator > 0)
        {
            _runnerDispatches.AddOrUpdate(key[..separator], 1, static (_, count) => count + 1);
        }
    }

    internal void ResetRunnerDispatch(NeuronId worker)
        => _runnerDispatches.TryRemove(worker.GrainKey, out _);

    internal AIWorkerRunnerExecution BlockRunnerExecution(NeuronId worker)
    {
        var execution = new AIWorkerRunnerExecution();

        if (!_runnerExecutions.TryAdd(worker.GrainKey, execution))
        {
            throw new InvalidOperationException($"Worker '{worker}' already has a runner execution probe.");
        }

        return execution;
    }

    internal AIWorkerRunnerExecution? TryGetRunnerExecution(GrainId runner)
    {
        var key = runner.Key.ToString();
        var separator = key.IndexOf(RunnerSeparator, StringComparison.Ordinal);

        return separator > 0
            && _runnerExecutions.TryGetValue(key[..separator], out var execution)
                ? execution
                : null;
    }

    internal void ResetRunnerExecution(NeuronId worker)
        => _runnerExecutions.TryRemove(worker.GrainKey, out _);

    internal AIWorkerRunnerLifetime PrepareRunnerLifetime(NeuronId worker)
    {
        var lifetime = new AIWorkerRunnerLifetime();

        if (!_runnerLifetimes.TryAdd(worker.GrainKey, lifetime))
        {
            throw new InvalidOperationException($"Worker '{worker}' already has a runner lifetime probe.");
        }

        return lifetime;
    }

    internal AIWorkerRunnerLifetime? TryEnterRunnerLifetime(GrainId runner, object? command)
    {
        var key = runner.Key.ToString();
        var separator = key.IndexOf(RunnerSeparator, StringComparison.Ordinal);

        if (separator <= 0
            || !_runnerLifetimes.TryGetValue(key[..separator], out var lifetime))
        {
            return null;
        }

        lifetime.Enter(RunnerCommandRunId(command));
        return lifetime;
    }

    internal void ResetRunnerLifetime(NeuronId worker)
        => _runnerLifetimes.TryRemove(worker.GrainKey, out _);

    internal AIWorkerRunnerCancellation BlockRunnerCancellation(NeuronId worker)
        => PrepareRunnerCancellation(worker, block: true, failure: null);

    internal AIWorkerRunnerCancellation FailRunnerCancellation(NeuronId worker, string failure)
        => PrepareRunnerCancellation(worker, block: false, failure);

    internal bool TryGetRunnerCancellation(GrainId runner, out AIWorkerRunnerCancellation cancellation)
    {
        var key = runner.Key.ToString();
        var separator = key.IndexOf(RunnerSeparator, StringComparison.Ordinal);

        if (separator > 0
            && _runnerCancellations.TryGetValue(key[..separator], out var found))
        {
            cancellation = found;
            return true;
        }

        cancellation = null!;
        return false;
    }

    internal void ResetRunnerCancellation(NeuronId worker)
        => _runnerCancellations.TryRemove(worker.GrainKey, out _);

    private AIWorkerRpcObservation AddRpc(
        NeuronId target,
        string method,
        bool block,
        bool replay = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        var observation = new AIWorkerRpcObservation(block, replay);

        if (!_rpc.TryAdd((target.ToGrainId(), method), observation))
        {
            throw new InvalidOperationException(
                $"RPC '{target}.{method}' already has an observation.");
        }

        return observation;
    }

    private AIWorkerRunnerCancellation PrepareRunnerCancellation(
        NeuronId worker,
        bool block,
        string? failure)
    {
        var cancellation = new AIWorkerRunnerCancellation(block, failure);

        if (!_runnerCancellations.TryAdd(worker.GrainKey, cancellation))
        {
            throw new InvalidOperationException($"Worker '{worker}' already has a runner cancellation probe.");
        }

        return cancellation;
    }

    private static Guid RunnerCommandRunId(object? command)
    {
        var run = command?.GetType().GetProperty("Run")?.GetValue(command)
            ?? throw new InvalidOperationException("The workflow runner command has no Run.");

        return (Guid)(run.GetType().GetProperty("RunId")?.GetValue(run)
            ?? throw new InvalidOperationException("The workflow run has no RunId."));
    }
}

internal sealed class AIWorkerRpcObservation(bool blockResponse, bool replay)
{
    private readonly TaskCompletionSource<bool> _invocationCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _replayReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _replayRelease =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _entryCount;
    private int _failureCount;
    private int _invocationCount;
    private int _replayStarted;

    internal Task InvocationCompleted => _invocationCompleted.Task;

    internal Task Completed => _completed.Task;

    internal int EntryCount => Volatile.Read(ref _entryCount);

    internal int FailureCount => Volatile.Read(ref _failureCount);

    internal int InvocationCount => Volatile.Read(ref _invocationCount);

    internal Exception? Failure { get; private set; }

    internal Task ReplayReady => _replayReady.Task;

    internal void Enter()
    {
        Interlocked.Increment(ref _entryCount);
    }

    internal void CompleteInvocation(Exception? failure)
    {
        Interlocked.Increment(ref _invocationCount);

        if (failure is not null)
        {
            Failure = failure;
            Interlocked.Increment(ref _failureCount);
        }

        _invocationCompleted.TrySetResult(true);
    }

    internal void ReadyReplay() => _replayReady.TrySetResult(true);

    internal bool TryBeginReplay()
        => replay && Interlocked.CompareExchange(ref _replayStarted, 1, 0) == 0;

    internal Task WaitForReplayReleaseAsync() => _replayRelease.Task;

    internal async Task WaitForResponseReleaseAsync()
    {
        if (blockResponse)
        {
            await _release.Task;
        }
    }

    internal void CompleteResponse()
        => _completed.TrySetResult(true);

    internal void Release() => _release.TrySetResult(true);

    internal void ReleaseReplay() => _replayRelease.TrySetResult(true);
}

internal sealed class AIWorkerContinuationMutation
{
    private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Entered => _entered.Task;

    internal AttemptCursor Mutate(AttemptCursor cursor)
    {
        _entered.TrySetResult(true);
        return cursor with { Revision = checked(cursor.Revision + 1) };
    }
}

internal sealed class AIWorkerContinuationGate
{
    private readonly TaskCompletionSource<bool> _entered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Entered => _entered.Task;

    internal Task Completed => _completed.Task;

    internal Exception? Failure { get; private set; }

    internal async Task EnterAsync()
    {
        _entered.TrySetResult(true);
        await _release.Task;
    }

    internal void RecordFailure(Exception failure) => Failure = failure;

    internal void Release() => _release.TrySetResult(true);

    internal void Complete() => _completed.TrySetResult(true);
}

internal sealed class AIWorkerCheckpointReadGate
{
    private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Entered => _entered.Task;

    internal async Task BlockAsync()
    {
        _entered.TrySetResult(true);
        await _release.Task;
    }

    internal void Release() => _release.TrySetResult(true);
}

internal sealed class AIWorkerRunnerExecution
{
    private readonly TaskCompletionSource<bool> _entered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Entered => _entered.Task;

    internal Task Completed => _completed.Task;

    internal Exception? Failure { get; private set; }

    internal async Task EnterAsync()
    {
        _entered.TrySetResult(true);
        await _release.Task;
    }

    internal void RecordFailure(Exception failure) => Failure = failure;

    internal void Release() => _release.TrySetResult(true);

    internal void Complete() => _completed.TrySetResult(true);
}

internal sealed class AIWorkerRunnerLifetime
{
    private readonly TaskCompletionSource<bool> _entered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Entered => _entered.Task;

    internal Task Completed => _completed.Task;

    internal Guid RunId { get; private set; }

    internal void Enter(Guid runId)
    {
        RunId = runId;
        _entered.TrySetResult(true);
    }

    internal void Complete() => _completed.TrySetResult(true);
}

internal sealed class AIWorkerRunnerCancellation(bool block, string? failure)
{
    private readonly TaskCompletionSource<bool> _entered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _entryCount;

    internal Task Entered => _entered.Task;

    internal int EntryCount => Volatile.Read(ref _entryCount);

    internal Guid RunId { get; private set; }

    internal async Task EnterAsync(Guid runId)
    {
        RunId = runId;
        Interlocked.Increment(ref _entryCount);
        _entered.TrySetResult(true);

        if (block)
        {
            await _release.Task;
        }

        if (failure is not null)
        {
            throw new InvalidOperationException(failure);
        }
    }

    internal void Release() => _release.TrySetResult(true);
}

internal sealed class AIWorkerJournalStorageProvider : IJournalStorageProvider
{
    private readonly VolatileJournalStorageProvider _inner = new();
    private readonly Dictionary<JournalId, InjectedFailure> _failures = [];
    private readonly ConcurrentDictionary<JournalId, int> _firedFailures = new();
    private readonly ConcurrentDictionary<JournalId, int> _writes = new();
    private readonly ConcurrentDictionary<JournalId, int> _completedWrites = new();
    private readonly ConcurrentDictionary<JournalId, AIWorkerScheduledWriteGate> _blockedWrites = new();
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
        => BlockWriteAfter(grain, completedWritesBeforeBlock: 0);

    internal AIWorkerWriteGate BlockWriteAfter(
        GrainId grain,
        int completedWritesBeforeBlock)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completedWritesBeforeBlock);
        var gate = new AIWorkerWriteGate();
        var scheduled = new AIWorkerScheduledWriteGate(
            completedWritesBeforeBlock,
            gate);

        if (!_blockedWrites.TryAdd(JournalId.FromGrainId(grain), scheduled))
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
        if (_blockedWrites.TryGetValue(journalId, out var scheduled)
            && scheduled.ShouldBlock()
            && _blockedWrites.TryRemove(journalId, out _))
        {
            await scheduled.Gate.BlockAsync(cancellationToken);
        }
    }

    private void AfterWrite(JournalId journalId)
        => _completedWrites.AddOrUpdate(journalId, 1, static (_, count) => count + 1);

    private sealed record InjectedFailure(int CompletedWritesBeforeFailure, string Message);

    private sealed class AIWorkerScheduledWriteGate(
        int completedWritesBeforeBlock,
        AIWorkerWriteGate gate)
    {
        private readonly object _lock = new();
        private int _remaining = completedWritesBeforeBlock;

        internal AIWorkerWriteGate Gate { get; } = gate;

        internal bool ShouldBlock()
        {
            lock (_lock)
            {
                if (_remaining == 0)
                {
                    return true;
                }

                _remaining--;
                return false;
            }
        }
    }

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

internal sealed class AIWorkerTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private readonly object _lock = new();
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _utcNow;
        }
    }

    internal void Advance(TimeSpan elapsed)
    {
        lock (_lock)
        {
            _utcNow += elapsed;
        }
    }
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
internal partial interface IAIWorkerModel : ILLM;

internal sealed class AIWorkerModel : Neuron, IAIWorkerModel
{
    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
        => ServiceProvider.GetRequiredService<AIWorkerTestProbes>().GateFor(Id.Owner).RespondAsync(messages);
}

[Alias("db.test.task-group-chat")]
internal partial interface ITaskGroupChat : IGroupChat
{
    [Alias("ReadDirectState")]
    Task<byte[]> ReadDirectStateAsync();

    [Alias("ReadWorkerState")]
    Task<byte[]> ReadWorkerStateAsync();

    [Alias("ReadDurableWorker")]
    Task<AIWorkerDurableObservation> ReadDurableWorkerAsync();

    [Alias("DeactivateWorker")]
    Task DeactivateWorkerAsync();
}

[GenerateSerializer]
[Alias("db.test.ai-worker-durable-observation")]
internal sealed record AIWorkerDurableObservation(
    [property: Id(0)] Guid ActivationId,
    [property: Id(1)] byte[] WorkerState,
    [property: Id(2)] int OutboxCount,
    [property: Id(3)] int CapturedCauseCount);

internal sealed class TaskGroupChat : GroupChat, ITaskGroupChat
{
    private const string DirectStateName = "ai.group-chat.session";
    private const string WorkerStateName = "ai.group-chat.worker";
    private const string OutboxName = "outbox";
    private const string CapturedCapabilityCausesName = "captured-capability-causes";
    private readonly Guid _activationId = Guid.NewGuid();

    protected override IReadOnlyList<Participant> Participants =>
        [Participant<IAIWorkerModel>(
            ServiceProvider.GetRequiredService<AIWorkerTestProbes>().DefinitionNameFor(Id.Owner, Id.Name))];

    protected override IReadOnlyList<ChatMessage> CreateMessages(Goal goal)
    {
        var request = Assert.IsType<AIWorkerGoal>(goal);

        return ServiceProvider.GetRequiredService<AIWorkerTestProbes>()
            .GateFor(Id.Owner)
            .SourceMessages(request.Prompt);
    }

    protected override Result CreateResult(IReadOnlyList<ChatMessage> messages)
        => new AIWorkerResult(
            messages.Last(message => message.Role == ChatRole.Assistant).Text,
            messages is not IList<ChatMessage> mutable || mutable.IsReadOnly);

    public Task<byte[]> ReadDirectStateAsync()
        => Task.FromResult(ReadState(DirectStateName));

    public Task<byte[]> ReadWorkerStateAsync()
        => Task.FromResult(ReadState(WorkerStateName));

    public Task<AIWorkerDurableObservation> ReadDurableWorkerAsync()
    {
        var workerState = ReadState(WorkerStateName);
        var outbox = ServiceProvider
            .GetRequiredKeyedService<IDurableList<byte[]>>(OutboxName);
        var capturedCauses = ServiceProvider
            .GetRequiredKeyedService<IDurableDictionary<Guid, byte[]>>(
                CapturedCapabilityCausesName);

        return Task.FromResult(new AIWorkerDurableObservation(
            _activationId,
            workerState,
            outbox.Count,
            capturedCauses.Count));
    }

    public Task DeactivateWorkerAsync()
    {
        DeactivateOnIdle();

        return Task.CompletedTask;
    }

    private byte[] ReadState(string name)
        => ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(name).Value?.ToArray() ?? [];
}

[Alias("db.test.empty-task-group-chat")]
internal partial interface IEmptyTaskGroupChat : IGroupChat;

internal sealed class EmptyTaskGroupChat : GroupChat, IEmptyTaskGroupChat
{
    protected override IReadOnlyList<Participant> Participants => [];

    protected override IReadOnlyList<ChatMessage> CreateMessages(Goal goal)
        => [new ChatMessage(ChatRole.User, "cannot run")];

    protected override Result CreateResult(IReadOnlyList<ChatMessage> messages)
        => throw new NotSupportedException();
}

[Alias("db.test.foreign-participant-task-group-chat")]
internal partial interface IForeignParticipantTaskGroupChat : IGroupChat;

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
}

[Alias("db.test.ai-worker-probe")]
[ClientEntryPoint]
internal partial interface IAIWorkerProbe : INeuron
{
    [Alias("Accept")]
    Task AcceptAsync(NeuronId worker, AttemptRequest request);

    [Alias("Respond")]
    Task<ChatResponse> RespondAsync(NeuronId worker, IReadOnlyList<ChatMessage> messages);

    [Alias("Cancel")]
    Task CancelAsync(NeuronId worker, AttemptCursor cursor);

    [Alias("Continue")]
    Task ContinueAsync(NeuronId worker, AttemptCursor cursor);

    [Alias("ReadDirectState")]
    Task<byte[]> ReadDirectStateAsync(NeuronId worker);

    [Alias("ReadWorkerState")]
    Task<byte[]> ReadWorkerStateAsync(NeuronId worker);

    [Alias("ReadDurableWorker")]
    Task<AIWorkerDurableObservation> ReadDurableWorkerAsync(NeuronId worker);

    [Alias("DeactivateWorker")]
    Task DeactivateWorkerAsync(NeuronId worker);

    [Alias("ReadJournal")]
    Task<JournalRead> ReadJournalAsync(NeuronId worker, JournalKind kind);
}

internal sealed class AIWorkerProbe : Neuron, IAIWorkerProbe
{
    public Task AcceptAsync(NeuronId worker, AttemptRequest request)
        => GrainFactory.GetGrain<IWorker>(worker.ToGrainId()).Accept(request);

    public Task<ChatResponse> RespondAsync(NeuronId worker, IReadOnlyList<ChatMessage> messages)
        => GrainFactory.GetGrain<IAgent>(worker.ToGrainId()).Respond(messages);

    public Task CancelAsync(NeuronId worker, AttemptCursor cursor)
        => GrainFactory.GetGrain<IWorker>(worker.ToGrainId()).Cancel(cursor);

    public Task ContinueAsync(NeuronId worker, AttemptCursor cursor)
        => GrainFactory.GetGrain<IWorker>(worker.ToGrainId()).Continue(cursor);

    public Task<byte[]> ReadDirectStateAsync(NeuronId worker)
        => GrainFactory.GetGrain<ITaskGroupChat>(worker.ToGrainId()).ReadDirectStateAsync();

    public Task<byte[]> ReadWorkerStateAsync(NeuronId worker)
        => GrainFactory.GetGrain<ITaskGroupChat>(worker.ToGrainId()).ReadWorkerStateAsync();

    public Task<AIWorkerDurableObservation> ReadDurableWorkerAsync(NeuronId worker)
        => GrainFactory.GetGrain<ITaskGroupChat>(worker.ToGrainId()).ReadDurableWorkerAsync();

    public Task DeactivateWorkerAsync(NeuronId worker)
        => GrainFactory.GetGrain<ITaskGroupChat>(worker.ToGrainId()).DeactivateWorkerAsync();

    public Task<JournalRead> ReadJournalAsync(NeuronId worker, JournalKind kind)
        => GrainFactory.GetGrain<INeuron>(worker.ToGrainId()).ReadJournal(kind, afterSequence: 0);
}

[GenerateSerializer]
[Alias("db.test.ai-checkpoint-rollback-observation")]
internal sealed record AIWorkerCheckpointRollbackObservation(
    [property: Id(0)] bool FailureObserved,
    [property: Id(1)] int ChildrenAfterFailure,
    [property: Id(2)] int ChildrenAfterRetry,
    [property: Id(3)] bool RetryPayloadReadable);

[Alias("db.test.ai-worker-checkpoint-harness")]
[ClientEntryPoint]
internal partial interface IAIWorkerCheckpointHarness : INeuron
{
    [Alias("FailThenRetry")]
    Task<AIWorkerCheckpointRollbackObservation> FailThenRetryAsync(
        NeuronId task,
        NeuronId worker,
        AttemptId attempt);

    [Alias("CrossDefinitionReadFails")]
    Task<bool> CrossDefinitionReadFailsAsync(
        NeuronId task,
        NeuronId worker,
        AttemptId attempt,
        string firstFingerprint,
        string secondFingerprint);
}

internal sealed class AIWorkerCheckpointHarness : Neuron, IAIWorkerCheckpointHarness
{
    private static readonly Type CheckpointInterface = RequiredAIType(
        "DigitalBrain.AI.IWorkflowCheckpointGrain");
    private static readonly Type CheckpointWrite = RequiredAIType(
        "DigitalBrain.AI.CheckpointWrite");
    private static readonly Type CheckpointStore = RequiredAIType(
        "DigitalBrain.AI.OrleansCheckpointStore");

    public async Task<AIWorkerCheckpointRollbackObservation> FailThenRetryAsync(
        NeuronId task,
        NeuronId worker,
        AttemptId attempt)
    {
        var (grain, sessionId) = Checkpoint(task, worker, attempt);
        var failureObserved = false;

        try
        {
            _ = await CreateAsync(grain, sessionId, [1, 2, 3]);
        }
        catch (InvalidOperationException failure)
            when (failure.Message.Contains("injected checkpoint write failure", StringComparison.Ordinal))
        {
            failureObserved = true;
        }

        var afterFailure = await IndexAsync(grain);
        var retry = await CreateAsync(grain, sessionId, [4, 5, 6]);
        var afterRetry = await IndexAsync(grain);
        var payload = await ReadAsync(grain, retry);

        return new(
            failureObserved,
            afterFailure.Length,
            afterRetry.Length,
            payload.SequenceEqual(new byte[] { 4, 5, 6 }));
    }

    public async Task<bool> CrossDefinitionReadFailsAsync(
        NeuronId task,
        NeuronId worker,
        AttemptId attempt,
        string firstFingerprint,
        string secondFingerprint)
    {
        var (grain, sessionId) = Checkpoint(task, worker, attempt);
        var protector = ServiceProvider.GetRequiredService<IDurablePayloadProtector>();
        var first = CreateStore(
            grain,
            sessionId,
            protector,
            ProtectionPurpose(sessionId, firstFingerprint));
        var second = CreateStore(
            grain,
            sessionId,
            protector,
            ProtectionPurpose(sessionId, secondFingerprint));
        var checkpoint = await first.CreateCheckpointAsync(
            sessionId,
            JsonSerializer.SerializeToElement(new { Value = "protected" }),
            parent: null);

        try
        {
            _ = await second.RetrieveCheckpointAsync(sessionId, checkpoint);
            return false;
        }
        catch (CryptographicException)
        {
            return true;
        }
    }

    private (object Grain, string SessionId) Checkpoint(
        NeuronId task,
        NeuronId worker,
        AttemptId attempt)
    {
        var source = Encoding.UTF8.GetBytes($"v1\n{worker}\n{task}\n{attempt.Value:D}");
        var hash = Convert.ToHexStringLower(SHA256.HashData(source));
        var grain = GrainFactory.GetGrain(
            CheckpointInterface,
            IdSpan.Create($"{worker.GrainKey}/workflow-checkpoint/{hash}"));

        return (grain, $"dbw_{hash}");
    }

    private static async Task<object> CreateAsync(
        object grain,
        string sessionId,
        byte[] payload)
    {
        var command = Activator.CreateInstance(
            CheckpointWrite,
            sessionId,
            payload,
            null)
            ?? throw new InvalidOperationException("The checkpoint command could not be constructed.");
        var invocation = CheckpointInterface.GetMethod("CreateAsync")?.Invoke(grain, [command])
            ?? throw new MissingMethodException(CheckpointInterface.FullName, "CreateAsync");
        var task = (Task)invocation;
        await task;

        return task.GetType().GetProperty("Result")?.GetValue(task)
            ?? throw new InvalidOperationException("Checkpoint creation returned no reference.");
    }

    private static async Task<object[]> IndexAsync(object grain)
    {
        var invocation = CheckpointInterface.GetMethod("IndexAsync")?.Invoke(grain, [null])
            ?? throw new MissingMethodException(CheckpointInterface.FullName, "IndexAsync");
        var task = (Task)invocation;
        await task;
        var result = task.GetType().GetProperty("Result")?.GetValue(task) as Array
            ?? throw new InvalidOperationException("Checkpoint indexing returned no array.");

        return [.. result.Cast<object>()];
    }

    private static async Task<byte[]> ReadAsync(object grain, object checkpoint)
    {
        var invocation = CheckpointInterface.GetMethod("ReadAsync")?.Invoke(grain, [checkpoint])
            ?? throw new MissingMethodException(CheckpointInterface.FullName, "ReadAsync");
        var task = (Task)invocation;
        await task;

        return (byte[])(task.GetType().GetProperty("Result")?.GetValue(task)
            ?? throw new InvalidOperationException("Checkpoint reading returned no payload."));
    }

    private static JsonCheckpointStore CreateStore(
        object grain,
        string sessionId,
        IDurablePayloadProtector protector,
        string purpose)
        => (JsonCheckpointStore)(Activator.CreateInstance(
            CheckpointStore,
            grain,
            sessionId,
            protector,
            purpose)
            ?? throw new InvalidOperationException("The checkpoint store could not be constructed."));

    private static string ProtectionPurpose(string sessionId, string fingerprint)
    {
        var type = RequiredAIType("DigitalBrain.AI.WorkflowCheckpointProtection");
        var purpose = type.GetMethod(
            "Purpose",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException(type.FullName, "Purpose");

        return (string)(purpose.Invoke(null, [sessionId, fingerprint])
            ?? throw new InvalidOperationException("The checkpoint protection purpose was null."));
    }

    private static Type RequiredAIType(string name)
        => typeof(AIModule).Assembly.GetType(name, throwOnError: true)
            ?? throw new InvalidOperationException($"AI type '{name}' could not be resolved.");
}

internal sealed class AIWorkerGate
{
    private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _firstRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _secondRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _secondEntry = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _answer;
    private readonly string _secondAnswer;
    private readonly bool _mutateSourceDuringEnumeration;
    private readonly bool _requirePromptMatch;
    private readonly ConcurrentDictionary<int, ChatMessage[]> _inputs = new();
    private int _entries;
    private int _active;
    private int _maximumConcurrency;
    private BoundaryMutationMessages? _source;
    private ChatMessage[] _observedInput = [];

    internal AIWorkerGate(
        string prompt,
        string answer,
        bool mutateSourceDuringEnumeration,
        bool requirePromptMatch,
        string? secondAnswer)
    {
        Prompt = prompt;
        _answer = answer;
        _secondAnswer = secondAnswer ?? answer;
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
        _inputs[entry] = [.. _observedInput.Select(message => message.Clone())];
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
            await (entry == 1 ? _firstRelease.Task : _secondRelease.Task);
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }

        return new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            entry == 1 ? _answer : _secondAnswer));
    }

    internal IReadOnlyList<ChatMessage> InputAt(int entry)
        => _inputs.TryGetValue(entry, out var messages)
            ? messages
            : throw new InvalidOperationException($"The model has no recorded entry '{entry}'.");

    internal void ReleaseFirst() => _firstRelease.TrySetResult(true);

    internal void ReleaseSecond() => _secondRelease.TrySetResult(true);

    internal void Release()
    {
        ReleaseFirst();
        ReleaseSecond();
    }

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

internal sealed class BlockingDisposalExecutor()
    : Executor<string>("blocking-disposal-executor"), IAsyncDisposable
{
    private readonly TaskCompletionSource<bool> _disposalEntered = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _disposalCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task DisposalEntered => _disposalEntered.Task;

    internal Task DisposalCompleted => _disposalCompletion.Task;

    public override ValueTask HandleAsync(
        string message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _disposalEntered.TrySetResult(true);

        return new ValueTask(_disposalCompletion.Task);
    }

    internal void FailDisposal(Exception failure)
        => _disposalCompletion.TrySetException(failure);

    internal void ReleaseDisposal()
        => _disposalCompletion.TrySetResult(true);
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
internal partial interface IRawCapabilityTarget : INeuron
{
    [Alias("Enter")]
    Task EnterAsync();
}

[Alias("db.test.raw-capability-target-control")]
[ClientEntryPoint]
internal partial interface IRawCapabilityTargetControl : INeuron
{
    [Alias("EntryCount")]
    Task<int> EntryCountAsync();
}

internal sealed class RawCapabilityTarget : Neuron, IRawCapabilityTarget, IRawCapabilityTargetControl
{
    private int _entries;

    public Task EnterAsync()
    {
        _entries++;
        return Task.CompletedTask;
    }

    public Task<int> EntryCountAsync() => Task.FromResult(_entries);
}

[Alias("db.test.kernel-client-entry-probe")]
[ClientEntryPoint]
internal partial interface IKernelClientEntryProbe : INeuron
{
    [Alias("Enter")]
    Task<int> EnterAsync();
}

internal sealed class KernelClientEntryTarget : Neuron, IKernelClientEntryProbe
{
    private int _entries;

    public Task<int> EnterAsync() => Task.FromResult(++_entries);
}
