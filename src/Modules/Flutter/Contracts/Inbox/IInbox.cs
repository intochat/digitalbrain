using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter;

[Alias("inbox")]
[Orleans.Metadata.DefaultGrainType("inbox")]
public interface IInbox : INeuron
{
    Task Appear(string text);
}
