using System.Text;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Confirmation;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Operations;
using DigitalBrain.Runtime.Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Operations;

/// <summary>
/// Empirical adversarial stress-tests for OperationNeuron concurrency,
/// detached background execution, invalid transitions, and StaleVersionException elimination.
/// </summary>
public sealed class OperationNeuronConcurrencyFacts(OperationSilo silo) : IClassFixture<OperationSilo>
{
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public async Task DetachedBackgroundRunner_WithRapidConcurrentProgressAndCancel_ZeroStaleVersionExceptions()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = silo.Connections.A;
        var b = silo.Connections.B;

        // Populate a folder with 50 files
        var folder = a.AddFolder(a.Root, "stress-files-" + Guid.NewGuid().ToString("N")[..8]);
        for (var i = 0; i < 50; i++)
        {
            a.AddFile(folder, $"file_{i:D3}.txt", Bytes($"Stress test content {i}"));
        }

        // Slow down writes to 20ms per file so background runner is actively executing
        b.Delay = TimeSpan.FromMilliseconds(20);
        try
        {
            var brain = "op-stress-detach";
            var operations = silo.Operations(brain);
            var copyPlan = WritePlan.Copy(folder, b.Inner.Root);
            var operation = await operations.Start(copyPlan, ct);
            var neuron = silo.Entity(brain, operation.Id);

            // Wait until detached runner starts reporting progress
            while ((await silo.Signals(brain, operation.Id, ct)).OfType<OperationProgress>().Count() < 2)
            {
                await Task.Delay(25, ct);
            }

            // Bombard the grain concurrently from 20 parallel tasks while runner is running
            var bombardTasks = Enumerable.Range(0, 20).Select(async idx =>
            {
                await Task.Yield();
                for (var step = 0; step < 10; step++)
                {
                    if (idx % 4 == 0)
                    {
                        await neuron.RequestCancel();
                    }
                    else
                    {
                        await neuron.Progress(0.01 * (idx + step));
                    }
                    await Task.Delay(5, ct);
                }
            });

            await Task.WhenAll(bombardTasks);

            // Wait for operation to reach terminal state
            var handle = (OperationHandle)operation;
            var finalResult = await handle.Completion.WaitAsync(TimeSpan.FromSeconds(30), ct);

            // Ensure valid terminal outcome (Cancelled or Succeeded) without crashing
            Assert.True(finalResult is OperationResult.Cancelled or OperationResult.Succeeded);

            var finalState = (await neuron.Get())!;
            Assert.Equal(OperationPhase.Done, finalState.Phase);
            Assert.NotNull(finalState.Result);

            // Verify journal signals consistency: OperationDone must be the very last signal
            var signals = await silo.Signals(brain, operation.Id, ct);
            Assert.IsType<OperationStarted>(signals[0]);
            Assert.IsType<OperationDone>(signals[^1]);
            Assert.Single(signals.OfType<OperationDone>());

            // Crucial: No OperationProgress signals may appear after OperationDone
            var doneIndex = signals.FindIndex(s => s is OperationDone);
            var progressAfterDone = signals.Skip(doneIndex + 1).OfType<OperationProgress>().Count();
            Assert.Equal(0, progressAfterDone);
        }
        finally
        {
            b.Delay = TimeSpan.Zero;
        }
    }

    [Fact]
    public async Task GrainConcurrency_RapidConcurrentCompleteCancelAndProgress_SealsCleanly()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = "op-stress-race";
        var a = silo.Connections.A;

        for (var run = 0; run < 5; run++)
        {
            var folder = a.AddFolder(a.Root, $"race-{run}");
            var operation = await silo.Operations(brain).Start(WritePlan.CreateFolder(folder, "sub"), ct);
            var neuron = silo.Entity(brain, operation.Id);

            // Race 30 tasks: Complete vs Cancel vs Progress
            var tasks = Enumerable.Range(0, 30).Select(async idx =>
            {
                await Task.Yield();
                if (idx < 10)
                {
                    return (object)await neuron.Complete(new OperationResult.Succeeded());
                }
                else if (idx < 20)
                {
                    return (object)await neuron.RequestCancel();
                }
                else
                {
                    return (object)await neuron.Progress(0.1 * (idx - 20));
                }
            });

            // Must not throw StaleVersionException or unhandled exceptions
            await Task.WhenAll(tasks);

            var state = (await neuron.Get())!;
            Assert.Equal(OperationPhase.Done, state.Phase);
            Assert.NotNull(state.Result);

            var signals = await silo.Signals(brain, operation.Id, ct);
            Assert.Single(signals.OfType<OperationDone>());
            Assert.IsType<OperationDone>(signals[^1]);
        }
    }

    [Fact]
    public async Task InvalidTransitions_OnLiveOrleansGrain_AreSafelyIgnoredWithoutCorruptingState()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = "op-invalid-turns";
        var a = silo.Connections.A;

        var operation = await silo.Operations(brain).Start(WritePlan.CreateFolder(a.Root, "test-invalid"), ct);
        var neuron = silo.Entity(brain, operation.Id);

        // Complete the operation cleanly
        var completedState = await neuron.Complete(new OperationResult.Succeeded());
        Assert.Equal(OperationPhase.Done, completedState.Phase);

        var signalsBefore = (await silo.Signals(brain, operation.Id, ct)).Count;

        // 1. Attempting Complete on an already Done grain returns existing state without new signals
        var repeatComplete = await neuron.Complete(new OperationResult.Failed("err", "should be ignored"));
        Assert.Equal(OperationPhase.Done, repeatComplete.Phase);
        Assert.IsType<OperationResult.Succeeded>(repeatComplete.Result);
        Assert.Equal(signalsBefore, (await silo.Signals(brain, operation.Id, ct)).Count);

        // 2. Attempting Progress on an already Done grain returns false
        var progressResult = await neuron.Progress(0.8);
        Assert.False(progressResult);
        Assert.Equal(signalsBefore, (await silo.Signals(brain, operation.Id, ct)).Count);

        // 3. Attempting RequestCancel on an already Done grain returns false
        var cancelResult = await neuron.RequestCancel();
        Assert.False(cancelResult);
        Assert.Equal(signalsBefore, (await silo.Signals(brain, operation.Id, ct)).Count);

        // 4. Calling Prepared on an already Done grain does nothing
        await neuron.Prepared(null);
        var afterPrepared = (await neuron.Get())!;
        Assert.Equal(OperationPhase.Done, afterPrepared.Phase);
        Assert.Equal(signalsBefore, (await silo.Signals(brain, operation.Id, ct)).Count);
    }

    [Fact]
    public async Task AwaitingApproval_RejectsProgressAndSurvivesMismatchedApprovalAnswer()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = "op-gate"; // configured with ConfirmationPolicy.Always
        var a = silo.Connections.A;

        var operation = await silo.Operations(brain).Start(WritePlan.CreateFolder(a.Root, "Approval-Awaited-Folder"), ct);
        var neuron = silo.Entity(brain, operation.Id);

        var state = (await neuron.Get())!;
        Assert.Equal(OperationPhase.AwaitingApproval, state.Phase);
        Assert.NotNull(state.ApprovalPlanId);

        var signalsBefore = (await silo.Signals(brain, operation.Id, ct)).Count;

        // 1. Progress is rejected while awaiting approval
        var progressAllowed = await neuron.Progress(0.5);
        Assert.False(progressAllowed);
        Assert.Equal(signalsBefore, (await silo.Signals(brain, operation.Id, ct)).Count);

        var inbox = NeuronHosting.Get<IConfirmationInbox>(silo.Services, IConfirmationRequests.Of(new BrainId(brain)));

        // 2. Resolving a nonexistent or mismatched plan id does not affect this operation
        try
        {
            await inbox.Resolve("wrong-plan-id", approve: true);
        }
        catch
        {
            // Expected if inbox throws or ignores nonexistent plan
        }

        var stateAfterMismatched = (await neuron.Get())!;
        Assert.Equal(OperationPhase.AwaitingApproval, stateAfterMismatched.Phase);
        Assert.Equal(signalsBefore, (await silo.Signals(brain, operation.Id, ct)).Count);

        // 3. Proper resolution through inbox as a person settles the operation
        using (DigitalBrain.Runtime.Orleans.Capabilities.RuntimeCallerContext.Stamp(
            DigitalBrain.Capabilities.CapabilityCallers.Of("person", "person", brain)))
        {
            await inbox.Resolve(state.ApprovalPlanId, approve: true);
        }

        // Wait for operation to complete
        var doneResult = await ((OperationHandle)operation).Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);
        if (doneResult is OperationResult.Failed f)
        {
            Assert.Fail($"Failed with code='{f.Code}', reason='{f.Message}'");
        }
        Assert.IsType<OperationResult.Succeeded>(doneResult);

        var finalState = (await neuron.Get())!;
        Assert.Equal(OperationPhase.Done, finalState.Phase);
        Assert.IsType<OperationResult.Succeeded>(finalState.Result);

        // Verify signals sequence
        var signals = await silo.Signals(brain, operation.Id, ct);
        Assert.IsType<OperationStarted>(signals[0]);
        Assert.Contains(signals, s => s is OperationAwaitingApproval);
        Assert.IsType<OperationDone>(signals[^1]);
    }

    [Fact]
    public async Task AwaitingApproval_WhenApprovedByNonPerson_FailsWithOnlyAPersonCanApproveThis()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = "op-gate";
        var a = silo.Connections.A;

        var operation = await silo.Operations(brain).Start(WritePlan.CreateFolder(a.Root, "Self-Approval-Folder"), ct);
        var neuron = silo.Entity(brain, operation.Id);

        var state = (await neuron.Get())!;
        Assert.Equal(OperationPhase.AwaitingApproval, state.Phase);
        Assert.NotNull(state.ApprovalPlanId);

        var inbox = NeuronHosting.Get<IConfirmationInbox>(silo.Services, IConfirmationRequests.Of(new BrainId(brain)));

        // Resolve without person caller stamp (e.g. app or unauthenticated)
        await inbox.Resolve(state.ApprovalPlanId, approve: true);

        // Wait for operation to complete
        var doneResult = await ((OperationHandle)operation).Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);
        var failed = Assert.IsType<OperationResult.Failed>(doneResult);
        Assert.Equal("declined", failed.Code);
        Assert.Equal("Only a person can approve this.", failed.Message);

        var finalState = (await neuron.Get())!;
        Assert.Equal(OperationPhase.Done, finalState.Phase);
        Assert.Equal("declined", ((OperationResult.Failed)finalState.Result!).Code);
    }
}
