using DigitalBrain.Product.Identity;
using DigitalBrain.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests.Execution;

internal static class ExecutionTestDriver
{
    public static async Task<NeuronReference<IExecution>> StartAndCompleteAsync(
        IDigitalBrain brain,
        ExecutionId executionId,
        WorkloadDescriptor workload,
        IReadOnlyList<ExecutionId>? relatedExecutions = null,
        CancellationToken cancellationToken = default)
    {
        var name = executionId.ToString();
        await brain.Get<IExecution>(name).SendAsync(
            new StartExecution(
                CommandId.New(),
                executionId,
                workload,
                relatedExecutions),
            cancellationToken);
        await AwaitCompletionAsync(brain, name);

        return brain.Get<IExecution>(name);
    }

    // Waits for ANY terminal lifecycle, then asserts it Completed — so a Failed
    // execution reports its Detail immediately instead of burning the full journal-wait
    // timeout and dying as an uninformative TimeoutException.
    public static async Task<ExecutionLifecycle> AwaitCompletionAsync(IDigitalBrain brain, string executionName)
    {
        var terminal = await JournalWait.ForAsync(
            brain.Get<IExecution>(executionName),
            JournalKind.Outgoing,
            static delivery => delivery.Signal is ExecutionLifecycle
            {
                Status: not ExecutionStatus.Running
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var lifecycle = Assert.IsType<ExecutionLifecycle>(terminal.Signal);
        Assert.True(lifecycle.Status == ExecutionStatus.Completed, lifecycle.Detail);
        return lifecycle;
    }
}
