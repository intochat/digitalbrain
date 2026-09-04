using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Chat;
using DigitalBrain.Testing;

namespace DigitalBrain.Simulation.Tests;

internal static class ChatTranscriptRead
{
    public static Task<ChatTranscript> ForGrainKeyAsync(
        BrainSimulation sim,
        string chatGrainKey,
        CancellationToken cancellationToken = default)
    {
        var id = NeuronId.FromGrainKey("chat", chatGrainKey);
        return ForAsync(sim.BrainFor(id.Owner.Value), id.Name, cancellationToken);
    }

    public static async Task<ChatTranscript> ForAsync(
        IDigitalBrain brain,
        string chatName,
        CancellationToken cancellationToken = default)
    {
        var read = await brain.Get<IChat>(chatName)
            .RequestAsync(new ReadTranscriptRequest(chatName), cancellationToken);
        return read.Transcript;
    }
}
