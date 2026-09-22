using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Inbox;

[Alias("inbox")]
[Orleans.Metadata.DefaultGrainType("inbox")]
public interface IInbox : INeuron
{
    Task Appear(string text);

    [ReadOnly, Alias("read")]
    Task<IReadOnlyList<string>> Read();
}