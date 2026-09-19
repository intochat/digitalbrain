using DigitalBrain.Contracts;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class GmailFacts
{
    [Fact]
    public async Task SendingAnEmailBroadcastsEmailSent()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync();
        var gmail = brain.Get<IGmail>("ada@intochat.com");

        await gmail.Send("ops@acme.com", "Invoice 1842", "Please pay.");

        var signals = await WaitUntil(
            () => brain.Signals(),
            all => all.OfType<EmailSent>().Any(),
            cancellation);
        var sent = Assert.Single(signals.OfType<EmailSent>());
        Assert.Equal("ada@intochat.com", sent.From);
        Assert.Equal("ops@acme.com", sent.To);
        Assert.Equal("Invoice 1842", sent.Subject);
        Assert.Equal("Please pay.", sent.Body);
        Assert.Equal(sent, Assert.Single(await gmail.Outbox()));
    }

    [Fact]
    public async Task IndependentBehaviorsReactToTheSameEmailSent()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync();
        var gmail = brain.Get<IGmail>("ada@intochat.com");
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var invoice = InvoiceFollowUp.Run(brain, run.Token);
        var audit = MailAudit.Run(brain, run.Token);

        await gmail.Send("friend@example.com", "Lunch", "Thursday?");
        await WaitUntil(
            async () =>
            {
                await gmail.Send("ops@acme.com", "Invoice 1842", "Please pay.");
                return (
                    await brain.Get<INotification>("ui").Read(),
                    await brain.Get<IMailLog>("audit").Read());
            },
            result => result.Item1.Count > 0 && result.Item2.Count > 0,
            cancellation);

        var notes = await brain.Get<INotification>("ui").Read();
        Assert.All(notes, note => Assert.Equal("invoice sent to ops@acme.com: Invoice 1842", note));
        Assert.Contains(await brain.Get<IMailLog>("audit").Read(), email => email.Subject == "Invoice 1842");

        await run.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invoice);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => audit);
    }

    private static async Task<T> WaitUntil<T>(Func<Task<T>> read, Func<T, bool> done, CancellationToken cancellation)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        T last = default!;
        while (DateTime.UtcNow < deadline)
        {
            last = await read();
            if (done(last))
            {
                return last;
            }

            await Task.Delay(20, cancellation);
        }

        throw new TimeoutException($"Condition not met. Last: {last}");
    }
}
