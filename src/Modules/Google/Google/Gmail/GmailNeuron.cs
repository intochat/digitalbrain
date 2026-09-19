using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Google;

[GrainType("gmail")]
public sealed class GmailNeuron : Neuron, IGmail
{
    public Task AcceptWatchPush(GmailWatchPush push)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(push.EmailAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(push.HistoryId);
        return PublishAsync(new MailReceived(push.EmailAddress, push.HistoryId));
    }
}
