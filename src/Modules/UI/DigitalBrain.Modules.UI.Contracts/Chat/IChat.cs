using DigitalBrain.Abstractions;
using DigitalBrain.Execution;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Chat;

[Alias("chat")]
public partial interface IChat :
    INeuron,
    IHandle<ReadTranscriptRequest>,
    IHandle<Note>,
    IHandle<KitCardOffer>
{
    [Alias(nameof(Send))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<TurnAccepted> Send(SendMessage message);

    [Alias(nameof(SendStreaming))]
    IAsyncEnumerable<ChatResponseUpdate> SendStreaming(
        SendMessage message,
        CancellationToken cancellationToken = default);

    [Alias(nameof(Cancel))]
    Task Cancel(CancelTurn command);

    [Alias(nameof(CompleteUserAction))]
    Task CompleteUserAction(AgentTurnContext context, string actionId, bool accepted);

    [Alias(nameof(Read))]
    Task<ChatTranscript> Read();

    [Alias(nameof(ReadTurns))]
    Task<IReadOnlyList<ChatTurnSnapshot>> ReadTurns();

    [Alias(nameof(ReadActiveExecution))]
    Task<ExecutionId?> ReadActiveExecution();

    [Alias(nameof(SetActiveExecution))]
    Task SetActiveExecution(ExecutionId? id);
}
