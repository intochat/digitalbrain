using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class AIContracts(ModuleFixture fixture)
{
    [Fact]
    public async Task TypedLlmReturnsTheScriptedResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        test.Chat().Reply("typed response");

        var response = await test.Client.Get<ILlama32>("typed-model").Respond(
            [new ChatMessage(ChatRole.User, "hello")]);

        Assert.Equal("typed response", response.Text);
        Assert.Single(test.Chat().Calls);
    }

    [Fact]
    public async Task DirectAgentResumesItsDurableSessionAfterRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        test.Chat().Reply("first response");
        test.Chat().Reply("second response");
        var driver = test.Client.Get<IModuleDriver>("direct-driver");
        var agent = test.Neuron<IModuleConcurrent>("direct-agent");

        Assert.Equal(
            "first response",
            await driver.RunDirectAgent(agent.Id, "first prompt"));

        await agent.RestartHostAsync(cancellationToken);

        Assert.Equal(
            "second response",
            await driver.RunDirectAgent(agent.Id, "second prompt"));

        Assert.Equal(2, test.Chat().Calls.Count);
        Assert.Contains(
            test.Chat().Calls[1].Messages,
            message => message.Text == "first response");
    }

    [Fact]
    public async Task GroupChatCompletesASupervisedTask()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        test.Chat().Reply("group result");
        var driver = test.Neuron<IModuleDriver>("group-success-driver");
        var task = test.Neuron<ITask>("group-success");
        var worker = test.Neuron<IModuleGroupChat>("group-success-worker");
        var succeeded = task.Incoming.NextAsync<AttemptSucceeded>(
            cancellationToken);

        _ = await StartGroupTask(
            test,
            driver,
            task.Id,
            worker.Id,
            "success",
            cancellationToken);

        var result = Assert.IsType<ModuleResult>((await succeeded).Synapse.Result);
        Assert.Equal("group result", result.Value);
    }

    [Fact]
    public async Task GroupChatFailureRecoversWithoutPublishingAFalseTaskFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        test.Chat().Fail("scripted group failure");
        test.Chat().Reply("recovered result");
        var driver = test.Neuron<IModuleDriver>("group-failure-driver");
        var task = test.Neuron<ITask>("group-failure");
        var worker = test.Neuron<IModuleGroupChat>("group-failure-worker");
        var succeeded = task.Incoming.NextAsync<AttemptSucceeded>(
            cancellationToken);

        _ = await StartGroupTask(
            test,
            driver,
            task.Id,
            worker.Id,
            "failure",
            cancellationToken);
        _ = await test.Chat().NextInvocation(cancellationToken);
        _ = await test.Chat().NextCompletion(cancellationToken);
        Assert.Empty(await task.Incoming.ReadAsync<AttemptFailed>(
            cancellationToken: cancellationToken));

        var read = driver.Outgoing.NextAsync<TaskObserved>(
            cancellationToken);
        await test.Client.SendAsync<IModuleDriver>(
            "group-failure-driver",
            new ReadModuleTask(task.Id));
        Assert.Equal(TaskState.Running, (await read).Synapse.Snapshot.State);

        var recoveredInvocation = test.Chat().NextInvocation(cancellationToken);
        await test.Clock.AdvanceAsync(TimeSpan.FromMinutes(2), cancellationToken);
        _ = await recoveredInvocation;
        Assert.Equal(2, test.Chat().Calls.Count);

        Assert.Equal(
            "recovered result",
            Assert.IsType<ModuleResult>((await succeeded).Synapse.Result).Value);
        Assert.Empty(await task.Incoming.ReadAsync<AttemptFailed>(
            cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task GroupChatCancellationFencesTheAttemptWithoutWaitingForTheBlockedProvider()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var block = test.Chat().Block();
        var driver = test.Neuron<IModuleDriver>("group-cancel-driver");
        var task = test.Neuron<ITask>("group-cancel");
        var worker = test.Neuron<IModuleGroupChat>("group-cancel-worker");
        Task<int>? providerCompletion = null;
        Task<CapabilityFailed>? delegatedFailure = null;

        try
        {
            var participantRequested = WaitForCapabilityRequest(
                worker.Outgoing,
                typeof(ILLM).FullName
                    ?? throw new InvalidOperationException(
                        "ILLM has no runtime contract name."),
                test.Neuron<ILlama32>("group-model").Id,
                cancellationToken);
            Assert.Equal(
                nameof(ITask.Start),
                (await StartGroupTask(
                    test,
                    driver,
                    task.Id,
                    worker.Id,
                    "cancel",
                    cancellationToken)).Operation);
            _ = await test.Chat().NextInvocation(cancellationToken);
            var request = await participantRequested;
            Assert.Equal(nameof(IAgent.Respond), request.Synapse.Method);
            delegatedFailure = WaitForCapabilityFailure(
                worker.Outgoing,
                request.SynapseId,
                cancellationToken);
            providerCompletion = test.Chat()
                .NextCompletion(cancellationToken)
                .AsTask();
            var readResponse = driver.Outgoing.NextAsync<TaskObserved>(
                cancellationToken);
            await test.Client.SendAsync<IModuleDriver>(
                "group-cancel-driver",
                new ReadModuleTask(task.Id));
            var current = await readResponse;
            Assert.Equal(nameof(ITask.Read), current.Synapse.Operation);

            var cancelled = task.Incoming.NextAsync<AttemptCancelled>(
                cancellationToken);
            var cancelResponse = driver.Outgoing.NextAsync<TaskObserved>(
                cancellationToken);
            await test.Client.SendAsync<IModuleDriver>(
                "group-cancel-driver",
                new CancelModuleTask(
                    task.Id,
                    new CancelTask(
                        CommandId.New(),
                        current.Synapse.Snapshot.Revision)));

            Assert.Equal(
                nameof(ITask.Cancel),
                (await cancelResponse).Synapse.Operation);
            Assert.Equal(task.Id, (await cancelled).Synapse.Task);
            Assert.False(providerCompletion.IsCompleted);
        }
        finally
        {
            block.Release();

            if (providerCompletion is not null)
            {
                _ = await providerCompletion;
            }

            if (delegatedFailure is not null)
            {
                _ = await delegatedFailure;
            }
        }
    }

    private static async Task<ObservedSynapse<CapabilityRequested>>
        WaitForCapabilityRequest(
            TestJournal journal,
            string contract,
            NeuronId target,
            CancellationToken cancellationToken)
    {
        while (true)
        {
            var observed = await journal.NextAsync<CapabilityRequested>(
                cancellationToken);

            if (observed.Synapse.Contract == contract
                && observed.Synapse.Target == target)
            {
                return observed;
            }
        }
    }

    private static async Task<CapabilityFailed> WaitForCapabilityFailure(
        TestJournal journal,
        SynapseId request,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var observed = await journal.NextAsync<Synapse>(
                cancellationToken);

            if (observed.Synapse is CapabilityFailed failed
                && failed.Request == request)
            {
                return failed;
            }
        }
    }

    private static async Task<TaskObserved> StartGroupTask(
        TestBrain test,
        TestNeuron<IModuleDriver> driver,
        NeuronId task,
        NeuronId worker,
        string script,
        CancellationToken cancellationToken)
    {
        var started = driver.Outgoing.NextAsync<TaskObserved>(
            cancellationToken);
        await test.Client.SendAsync<IModuleDriver>(
            driver.Id.Name,
            new StartModuleTask(
                task,
                new StartTask(
                    CommandId.New(),
                    new ModuleGoal(script),
                    worker,
                    new TaskPolicy(1, TimeSpan.Zero, null))));
        return (await started).Synapse;
    }
}
