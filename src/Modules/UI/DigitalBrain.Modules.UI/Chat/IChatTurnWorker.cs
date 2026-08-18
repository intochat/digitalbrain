using DigitalBrain.Chat;

namespace DigitalBrain.UI;

// The chat's turn attempt as ONE awaited grain call: Chat fires RunAsync off its own
// activation and settles the durable turn from the returned result (or the thrown
// failure). Cancelling the call's token is the turn-scoped cancel — grain calls cannot
// be aborted, so the worker observes the propagated token instead.
[Alias("chat.turn-worker")]
internal interface IChatTurnWorker : IGrainWithStringKey
{
    [Alias(nameof(RunAsync))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<ChatTurnResult> RunAsync(ChatTurnGoal goal, CancellationToken cancellationToken = default);
}
