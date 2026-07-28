using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tasks.Tests;

public sealed partial class TaskLifecycle(TasksFixture fixture)
{
    private static StartTask StartCommand(Goal goal, NeuronId worker, TaskPolicy? policy = null)
        => new(CommandId.New(), goal, worker, policy ?? TaskFixtures.SingleAttempt);

    private static async Task<(
        TestNeuron<IWorker> Worker,
        TestNeuron<ITask> Task,
        TaskSnapshot Started)> StartAsync(
        TestBrain brain,
        string name,
        Goal goal,
        TaskPolicy? policy = null)
    {
        var worker = brain.Neuron<IWorker>($"{name}-worker");
        var task = brain.Neuron<ITask>($"{name}-task");
        var started = await task.Reference.Start(StartCommand(goal, worker.Id, policy));
        return (worker, task, started);
    }

    private static async Task<TaskSnapshot> WaitForStateAsync(
        TestNeuron<ITask> task,
        TaskState expected,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await task.Reference.Read();
            if (snapshot.State == expected)
            {
                return snapshot;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }

        var final = await task.Reference.Read();
        throw new TimeoutException(
            $"Task '{task.Id}' stayed in {final.State} instead of becoming {expected}.");
    }

    private static async Task<TaskSnapshot> AcceptThenRunningAsync(TestNeuron<ITask> task, CancellationToken cancellationToken)
    {
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        return await WaitForStateAsync(task, TaskState.Running, cancellationToken);
    }

    private static void AssertAttempt<TFact>(
        ObservedSynapse<TFact> observed,
        NeuronId task,
        NeuronId worker,
        AttemptId? attempt,
        long revision)
        where TFact : AttemptFact
    {
        Assert.Equal(task, observed.Synapse.Task);
        Assert.Equal(worker, observed.Synapse.Worker);
        Assert.Equal(attempt, observed.Synapse.Attempt);
        Assert.Equal(revision, observed.Synapse.Revision);
        Assert.Equal(worker, observed.Caller);
    }

    private static void AssertReceipt(TaskSnapshot expected, TaskSnapshot actual)
    {
        Assert.Equal(expected.Goal, actual.Goal);
        Assert.Equal(expected.Worker, actual.Worker);
        Assert.Equal(expected.Policy, actual.Policy);
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.ActiveAttempt, actual.ActiveAttempt);
        Assert.Equal(expected.Blocker, actual.Blocker);
        Assert.Equal(expected.Result, actual.Result);
        Assert.Equal(expected.Failure, actual.Failure);
        Assert.Equal(expected.Evidence, actual.Evidence);
        Assert.Equal(expected.RetryOf, actual.RetryOf);
    }
}
