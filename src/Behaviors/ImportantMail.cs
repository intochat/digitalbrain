using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter.Inbox;
using DigitalBrain.Google.Gmail;

namespace DigitalBrain.Behaviors;

public sealed class ImportantMail(IDigitalBrain brain, string email) : IBehavior
{
    public async Task RunAsync(CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var gmail = brain.Get<IGmail>(email);
        var inbox = brain.Get<IInbox>("ui");
        await foreach (var message in brain.On<GmailMessageArrived>(gmail, cancellation))
        {
            if (!message.Subject.Contains("Important", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await inbox.Appear(message.Subject);
        }
    }
}