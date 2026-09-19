using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter;

[GrainType("inbox")]
internal sealed class InboxNeuron : Neuron, IInbox
{
    private const int Keep = 50;
    private readonly List<string> _lines = [];

    public Task Appear(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        _lines.Add(text);
        if (_lines.Count > Keep)
        {
            _lines.RemoveRange(0, _lines.Count - Keep);
        }

        return PublishAsync(new InboxAppeared(this.GetPrimaryKeyString(), text));
    }

    [ReadOnly]
    public Task<IReadOnlyList<string>> Read() => Task.FromResult<IReadOnlyList<string>>(_lines);
}
