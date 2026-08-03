using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class GrainCallerContextIsolation
{
    [Fact(DisplayName =
        "GrainCallerContext nested Enter restores outer caller after Dispose and after exception")]
    public void NestedEnterRestoresOuterAfterDisposeAndException()
    {
        var owner = new OwnerId("caller-context-owner");
        var outer = NeuronId.For<IWorker>(owner, "outer-worker");
        var inner = NeuronId.For<IWorker>(owner, "inner-worker");

        Assert.False(GrainCallerContext.TryGetNeuronId(out _));

        using (GrainCallerContext.Enter(outer.ToGrainId()))
        {
            Assert.True(GrainCallerContext.TryGetNeuronId(out var whileOuter));
            Assert.Equal(outer, whileOuter);

            using (GrainCallerContext.Enter(inner.ToGrainId()))
            {
                Assert.True(GrainCallerContext.TryGetNeuronId(out var whileInner));
                Assert.Equal(inner, whileInner);
            }

            Assert.True(GrainCallerContext.TryGetNeuronId(out var restoredOuter));
            Assert.Equal(outer, restoredOuter);

            try
            {
                using (GrainCallerContext.Enter(inner.ToGrainId()))
                {
                    Assert.True(GrainCallerContext.TryGetNeuronId(out var nested));
                    Assert.Equal(inner, nested);
                    throw new InvalidOperationException("nested-scope-throws");
                }
            }
            catch (InvalidOperationException failure)
            {
                Assert.Equal("nested-scope-throws", failure.Message);
            }

            Assert.True(GrainCallerContext.TryGetNeuronId(out var afterException));
            Assert.Equal(outer, afterException);
        }

        Assert.False(GrainCallerContext.TryGetNeuronId(out _));
    }

    [Fact(DisplayName =
        "GrainCallerContext concurrent async Enter scopes stay isolated and restore on dispose")]
    public async Task ConcurrentAsyncEnterScopesStayIsolatedAndRestore()
    {
        var owner = new OwnerId("caller-context-concurrent");
        var left = NeuronId.For<IWorker>(owner, "left-worker");
        var right = NeuronId.For<IWorker>(owner, "right-worker");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leftEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rightEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<NeuronId[]> RunSideAsync(NeuronId id, TaskCompletionSource entered)
        {
            using (GrainCallerContext.Enter(id.ToGrainId()))
            {
                Assert.True(GrainCallerContext.TryGetNeuronId(out var immediately));
                Assert.Equal(id, immediately);
                entered.TrySetResult();

                await gate.Task.ConfigureAwait(false);

                Assert.True(GrainCallerContext.TryGetNeuronId(out var afterAwait));
                Assert.Equal(id, afterAwait);
                return [immediately, afterAwait];
            }
        }

        var leftTask = RunSideAsync(left, leftEntered);
        var rightTask = RunSideAsync(right, rightEntered);

        await Task.WhenAll(leftEntered.Task, rightEntered.Task);
        gate.TrySetResult();

        var leftSeen = await leftTask;
        var rightSeen = await rightTask;

        Assert.All(leftSeen, seen => Assert.Equal(left, seen));
        Assert.All(rightSeen, seen => Assert.Equal(right, seen));
        Assert.False(GrainCallerContext.TryGetNeuronId(out _));
    }
}
