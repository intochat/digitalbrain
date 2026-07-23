using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using DigitalBrain.Security;
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
    [Fact(DisplayName = "GroupChat advances one durable Lockstep superstep per Task revision without replaying the participant")]
    public async Task GroupChatAdvancesOneDurableSuperstepPerRevisionWithoutReplayingParticipant()
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
        var cluster = await StartWorkerClusterAsync(journals, clock);
        AIWorkerLogProvider.Clear();

        var owner = new OwnerId("ai-worker-reminder-recovery");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        AIWorkerRunnerDispatchProbe.Reset(workerId);
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var reminder = cluster.Client.GetGrain<IReminderProbe>(
            NeuronId.For<ReminderProbe>(owner, "reminder-probe").ToGrainId());
        var gate = AIWorkerGate.Prepare(
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
            Assert.Equal(2, AIWorkerRunnerDispatchProbe.EntriesFor(workerId));

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
            Assert.Equal(2, AIWorkerRunnerDispatchProbe.EntriesFor(workerId));
            Assert.Equal(1, gate.EntryCount);

            replacementWrite.Release();
            await WaitUntilAsync(
                () => journals.CompletedWrites(workerId.ToGrainId()) > writesBeforeReplacement,
                "The recovered WorkflowRun replacement was not committed.");
            await WaitUntilAsync(
                () => AIWorkerRunnerDispatchProbe.EntriesFor(workerId) == 3,
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
        var cluster = await StartWorkerClusterAsync(journals, clock);
        AIWorkerLogProvider.Clear();

        var owner = new OwnerId("ai-worker-reminder-write-failure");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var workerGrain = workerId.ToGrainId();
        AIWorkerRunnerDispatchProbe.Reset(workerId);
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var reminder = cluster.Client.GetGrain<IReminderProbe>(
            NeuronId.For<ReminderProbe>(owner, "reminder-probe").ToGrainId());
        var gate = AIWorkerGate.Prepare(
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
            Assert.Equal(2, AIWorkerRunnerDispatchProbe.EntriesFor(workerId));

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
            Assert.Equal(2, AIWorkerRunnerDispatchProbe.EntriesFor(workerId));

            journals.ClearFailure(workerGrain);
            await reminder.ExpediteAsync(workerId, recoveryReminder);
            _ = await ReadJournalUntilAsync(
                probe,
                workerId,
                journal => journal.Delta.Count(delivery =>
                    delivery.Synapse is CapabilityRequested request
                    && request.Target == NeuronId.For<IAIWorkerModel>(owner, "worker")) == 2);
            Assert.Equal(3, AIWorkerRunnerDispatchProbe.EntriesFor(workerId));

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
        var cluster = await StartWorkerClusterAsync();
        AIWorkerLogProvider.Clear();

        var owner = new OwnerId("ai-worker-cancel-late-output");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        AIWorkerRunnerDispatchProbe.Reset(workerId);
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var reminder = cluster.Client.GetGrain<IReminderProbe>(
            NeuronId.For<ReminderProbe>(owner, "reminder-probe").ToGrainId());
        var gate = AIWorkerGate.Prepare(
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
            Assert.Equal(2, AIWorkerRunnerDispatchProbe.EntriesFor(workerId));

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
                        nameof(IWorker.ContinueAsync),
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
            Assert.Equal(2, AIWorkerRunnerDispatchProbe.EntriesFor(workerId));
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
        var cluster = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-cancel-wrong-caller");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "wrong-caller").ToGrainId());
        var gate = AIWorkerGate.Prepare(
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
        var cluster = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-continue-wrong-caller");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "wrong-caller").ToGrainId());
        var gate = AIWorkerGate.Prepare(
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
        var cluster = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-continue-future-revision");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        AIWorkerRunnerDispatchProbe.Reset(workerId);
        var mutation = AIWorkerContinuationMutationProbe.Prepare(workerId);
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var gate = AIWorkerGate.Prepare(
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
            Assert.Equal(1, AIWorkerRunnerDispatchProbe.EntriesFor(workerId));
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
            AIWorkerContinuationMutationProbe.Reset(workerId);
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
        var cluster = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-direct-awaiting-continuation");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var mutation = AIWorkerContinuationMutationProbe.Prepare(workerId);
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var probe = cluster.Client.GetGrain<IAIWorkerProbe>(
            NeuronId.For<AIWorkerProbe>(owner, "probe").ToGrainId());
        var gate = AIWorkerGate.Prepare(
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
            AIWorkerContinuationMutationProbe.Reset(workerId);
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

    [Fact(DisplayName = "a failed checkpoint adoption commit cannot leak a cleared ActiveRun or Task fact")]
    public async Task FailedCheckpointAdoptionCommitRollsBackWorkerStateAndTaskFact()
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
        var cluster = await StartWorkerClusterAsync();
        var owner = new OwnerId("ai-worker-checkpoint-read-order");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ITaskGroupChat>(owner, "worker");
        var checkpointRead = AIWorkerCheckpointReadProbe.Block(workerId);
        var driver = cluster.Client.GetGrain<ITaskDriver>(
            NeuronId.For<TaskDriver>(owner, "ai-worker-driver").ToGrainId());
        var task = new TaskTestClient(taskId, driver);
        var model = AIWorkerGate.Prepare(
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
            AIWorkerCheckpointReadProbe.Reset(workerId);
            model.Release();
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

    private static async Task<InProcessTestCluster> StartWorkerClusterAsync(
        IJournalStorageProvider? journalStorage = null,
        TimeProvider? clock = null)
    {
        var builder = new InProcessTestClusterBuilder(1);

        builder.ConfigureSilo((_, silo) =>
        {
            silo.Configuration[DurablePayloadProtector.ConfigurationKey] =
                Convert.ToBase64String(new byte[32]);
            silo.AddDigitalBrain("ai-worker-contracts");
            AIModule.Configure(silo);
            silo.UseInMemoryReminderService();
            silo.Services.AddSingleton<IJournalStorageProvider>(
                journalStorage ?? new VolatileJournalStorageProvider());
            silo.Services.AddSingleton<ILoggerProvider>(AIWorkerLogProvider.Instance);
            silo.AddIncomingGrainCallFilter<AIWorkerRunnerDispatchFilter>();

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

        return cluster;
    }
}

internal sealed class AIWorkerRunnerDispatchFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (AIWorkerContinuationMutationProbe.TryMutate(
                context.TargetId,
                context.InterfaceMethod?.Name,
                context.Request.GetArgumentCount() == 1
                    ? context.Request.GetArgument(0)
                    : null,
                out var mutated))
        {
            context.Request.SetArgument(0, mutated);
        }

        if (AIWorkerCheckpointReadProbe.TryGet(
                context.TargetId,
                context.InterfaceMethod?.Name,
                out var checkpointRead))
        {
            await checkpointRead.BlockAsync();
        }

        if (string.Equals(
                context.TargetId.Type.ToString(),
                "ai-workflow-runner",
                StringComparison.Ordinal)
            && string.Equals(
                context.InterfaceMethod?.Name,
                "ExecuteAsync",
                StringComparison.Ordinal))
        {
            AIWorkerRunnerDispatchProbe.Record(context.TargetId);
        }

        await context.Invoke();
    }
}

internal static class AIWorkerContinuationMutationProbe
{
    private static readonly ConcurrentDictionary<GrainId, AIWorkerContinuationMutation> Mutations = new();

    internal static AIWorkerContinuationMutation Prepare(NeuronId worker)
    {
        var mutation = new AIWorkerContinuationMutation();

        if (!Mutations.TryAdd(worker.ToGrainId(), mutation))
        {
            throw new InvalidOperationException($"Worker '{worker}' already has a continuation mutation.");
        }

        return mutation;
    }

    internal static bool TryMutate(
        GrainId target,
        string? method,
        object? argument,
        out AttemptCursor mutated)
    {
        if (string.Equals(method, nameof(IWorker.ContinueAsync), StringComparison.Ordinal)
            && argument is AttemptCursor cursor
            && Mutations.TryGetValue(target, out var mutation))
        {
            mutated = mutation.Mutate(cursor);
            return true;
        }

        mutated = null!;
        return false;
    }

    internal static void Reset(NeuronId worker)
        => Mutations.TryRemove(worker.ToGrainId(), out _);
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

internal static class AIWorkerCheckpointReadProbe
{
    private const string CheckpointSeparator = "/workflow-checkpoint/";
    private static readonly ConcurrentDictionary<string, AIWorkerCheckpointReadGate> Gates = new(StringComparer.Ordinal);

    internal static AIWorkerCheckpointReadGate Block(NeuronId worker)
    {
        var gate = new AIWorkerCheckpointReadGate();

        if (!Gates.TryAdd(worker.GrainKey, gate))
        {
            throw new InvalidOperationException($"Worker '{worker}' already has a checkpoint read gate.");
        }

        return gate;
    }

    internal static bool TryGet(
        GrainId target,
        string? method,
        out AIWorkerCheckpointReadGate gate)
    {
        var key = target.Key.ToString();
        var separator = key.IndexOf(CheckpointSeparator, StringComparison.Ordinal);

        if (string.Equals(method, "ReadAsync", StringComparison.Ordinal)
            && separator > 0
            && Gates.TryGetValue(key[..separator], out var found))
        {
            gate = found;
            return true;
        }

        gate = null!;
        return false;
    }

    internal static void Reset(NeuronId worker)
        => Gates.TryRemove(worker.GrainKey, out _);
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

internal static class AIWorkerRunnerDispatchProbe
{
    private const string RunnerSeparator = "/workflow-run/";
    private static readonly ConcurrentDictionary<string, int> Entries = new(StringComparer.Ordinal);

    internal static int EntriesFor(NeuronId worker)
        => Entries.GetValueOrDefault(worker.GrainKey);

    internal static void Record(GrainId runner)
    {
        var key = runner.Key.ToString();
        var separator = key.IndexOf(RunnerSeparator, StringComparison.Ordinal);

        if (separator > 0)
        {
            Entries.AddOrUpdate(key[..separator], 1, static (_, count) => count + 1);
        }
    }

    internal static void Reset(NeuronId worker)
        => Entries.TryRemove(worker.GrainKey, out _);
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
}

[Alias("db.test.ai-worker-probe")]
[ClientEntryPoint]
internal interface IAIWorkerProbe : INeuron
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

    [Alias("ReadJournal")]
    Task<JournalRead> ReadJournalAsync(NeuronId worker, JournalKind kind);
}

internal sealed class AIWorkerProbe : Neuron, IAIWorkerProbe
{
    public Task AcceptAsync(NeuronId worker, AttemptRequest request)
        => GrainFactory.GetGrain<IWorker>(worker.ToGrainId()).AcceptAsync(request);

    public Task<ChatResponse> RespondAsync(NeuronId worker, IReadOnlyList<ChatMessage> messages)
        => GrainFactory.GetGrain<IAgent>(worker.ToGrainId()).RespondAsync(messages);

    public Task CancelAsync(NeuronId worker, AttemptCursor cursor)
        => GrainFactory.GetGrain<IWorker>(worker.ToGrainId()).CancelAsync(cursor);

    public Task ContinueAsync(NeuronId worker, AttemptCursor cursor)
        => GrainFactory.GetGrain<IWorker>(worker.ToGrainId()).ContinueAsync(cursor);

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

    private AIWorkerGate(
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

    internal static AIWorkerGate Prepare(
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
