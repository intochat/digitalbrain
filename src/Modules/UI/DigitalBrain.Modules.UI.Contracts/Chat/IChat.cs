using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Chat;

[Alias("ui.chat")]
public interface IChat : INeuron
{
    /// <summary>Sends a message and returns the turn id as the receipt, equal to the scheduled work id.</summary>
    [Alias("send")]
    Task<Accepted<SignalId>> Send(SendMessage message, CancellationToken cancellationToken = default);

    /// <summary>Cancels a running turn by its turn id and stops the responder working on it.</summary>
    [Alias("cancel")]
    Task<Accepted<SignalId>> Cancel(CancelTurn command, CancellationToken cancellationToken = default);

    /// <summary>Reads the chat transcript up to the requested turn limit.</summary>
    [ReadOnly, Alias("transcript")]
    Task<ChatTranscript> ReadTranscript(ReadTranscript query);

    /// <summary>Reads chat turn snapshots up to the requested turn limit.</summary>
    [ReadOnly, Alias("turns")]
    Task<ChatTurns> ReadTurns(ReadTurns query);

    /// <summary>Reads a chat turn snapshot by its scheduled work id.</summary>
    [ReadOnly, Alias("turn")]
    Task<ChatTurnSnapshot?> ReadTurn(ReadTurn query);
}
