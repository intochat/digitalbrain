using DigitalBrain.Contracts;
using DigitalBrain.Testing;

await using var brain = await DigitalBrainSimulation.StartAsync();
await InvoiceFollowUp.Run(brain, CancellationToken.None);

public static class InvoiceFollowUp
{
    public static async Task Run(IDigitalBrain brain, CancellationToken cancellation)
    {
        var gmail = brain.Get<IGmail>("ada@intochat.com");
        var ui = brain.Get<INotification>("ui");

        await foreach (var sent in brain.On<EmailSent>(gmail, cancellation))
        {
            if (!sent.Subject.Contains("Invoice", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await ui.Notify($"invoice sent to {sent.To}: {sent.Subject}");
        }
    }
}

public static class MailAudit
{
    public static async Task Run(IDigitalBrain brain, CancellationToken cancellation)
    {
        var gmail = brain.Get<IGmail>("ada@intochat.com");
        var log = brain.Get<IMailLog>("audit");

        await foreach (var sent in brain.On<EmailSent>(gmail, cancellation))
        {
            await log.Append(sent);
        }
    }
}
