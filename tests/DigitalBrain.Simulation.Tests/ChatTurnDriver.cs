using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Chat;
using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

internal static class ChatTurnDriver
{
    private static readonly TimeSpan TurnTimeout = TimeSpan.FromSeconds(60);

    // Waits for ANY terminal turn lifecycle, then asserts it Completed — so a Failed or
    // Cancelled turn reports its Detail immediately instead of burning the full wait
    // timeout and dying as an uninformative TimeoutException.
    public static async Task<TurnLifecycle> AwaitCompletedTurnAsync(IDigitalBrain brain, string chatName)
    {
        var terminal = await JournalWait.ForAsync(
            brain.Get<IChat>(chatName),
            JournalKind.Outgoing,
            static delivery => delivery.Signal is TurnLifecycle
            {
                Status: ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled
            },
            TurnTimeout,
            cancellationToken: TestContext.Current.CancellationToken);

        var lifecycle = Assert.IsType<TurnLifecycle>(terminal.Signal);
        Assert.True(lifecycle.Status == ChatTurnStatus.Completed, lifecycle.Detail);
        return lifecycle;
    }
}
