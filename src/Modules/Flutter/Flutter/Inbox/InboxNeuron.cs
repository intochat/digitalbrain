using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Flutter;

[GrainType("inbox")]
internal sealed class InboxNeuron : Neuron, IInbox
{
    public Task Appear(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return PublishAsync(new InboxAppeared(this.GetPrimaryKeyString(), text));
    }
}
