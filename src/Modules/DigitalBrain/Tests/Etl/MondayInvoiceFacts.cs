using DigitalBrain.Contracts;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class MondayInvoiceFacts
{
    [Fact]
    public async Task MondayZipLoadsOnlyInvoicesAndConfirmsTheyAreInTheDatabase()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync();
        await brain.Get<IHttp>("http").Seed(InvoiceZip.Url, InvoiceZip.Create());

        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var etl = MondayInvoices.Run(brain, run.Token);
        var check = DatabaseCheck.Run(brain, run.Token);
        var week = brain.Get<IMonday>("week");
        var db = brain.Get<IDatabase>("db");

        var notes = await WaitUntil(
            async () =>
            {
                await week.Tick();
                return (
                    await db.Query("invoices"),
                    await brain.Get<INotification>("ui").Read(),
                    (await brain.Signals()).OfType<DatabaseAdded>().ToArray());
            },
            result => result.Item1.Count >= 2 && result.Item2.Count > 0 && result.Item3.Length > 0,
            cancellation);

        Assert.Equal(
            [new InvoiceRow("INV-1", 100m), new InvoiceRow("INV-2", 250m)],
            notes.Item1);
        Assert.DoesNotContain(notes.Item1, row => row.Id == "N-1");
        Assert.All(notes.Item2, note => Assert.Equal("invoices has INV-1,INV-2", note));
        Assert.All(notes.Item3, added =>
        {
            Assert.Equal("invoices", added.Table);
            Assert.Equal(["INV-1", "INV-2"], added.Ids);
        });

        await run.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => etl);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
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
