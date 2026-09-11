using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.UI;

[Alias("ui.transcript")]
public interface ITranscript : INeuron
{
    /// <summary>Appends an entry and returns the transcript instance name.</summary>
    [Alias("append")]
    Task<Accepted<string>> Append(AppendTranscript command, CancellationToken cancellationToken = default);

    /// <summary>Reads the retained transcript entries.</summary>
    [ReadOnly, Alias("read")]
    Task<TranscriptState> Read();
}
