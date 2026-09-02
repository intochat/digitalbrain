using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Client;
using DigitalBrain.Execution;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests.Execution;

internal static class ExecutionTestDriver
{
    public static async Task<IExecution> StartAndCompleteAsync(
        IDigitalBrain brain,
        ExecutionId executionId,
        WorkloadDescriptor workload,
        ExecutionDriverKind driver,
        IReadOnlyList<CapabilityId> grants,
        IReadOnlyList<ExecutionId>? relatedExecutions = null,
        CancellationToken cancellationToken = default)
    {
        var name = executionId.ToString();
        var execution = brain.GetGrainProxy<IExecution>(name);

        await execution.HandleAsync(
            new StartExecution(CommandId.New(), executionId, workload, driver, grants, relatedExecutions),
            cancellationToken);
        await AwaitCompletionAsync(brain, name);

        return execution;
    }

    // Waits for ANY terminal lifecycle, then asserts it Completed — so a Failed or Cancelled
    // execution reports its Detail immediately instead of burning the full journal-wait
    // timeout and dying as an uninformative TimeoutException.
    public static async Task<ExecutionLifecycle> AwaitCompletionAsync(IDigitalBrain brain, string executionName)
    {
        var terminal = await JournalWait.ForAsync(
            brain.Get<IExecution>(executionName),
            JournalKind.Outgoing,
            static delivery => delivery.Signal is ExecutionLifecycle
            {
                Status: not (ExecutionStatus.Pending or ExecutionStatus.Running or ExecutionStatus.AwaitingApproval)
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var lifecycle = Assert.IsType<ExecutionLifecycle>(terminal.Signal);
        Assert.True(lifecycle.Status == ExecutionStatus.Completed, lifecycle.Detail);
        return lifecycle;
    }
}
