using DigitalBrain.Contracts;
using DigitalBrain.Testing;

await using var brain = await DigitalBrainSimulation.StartAsync();
await MondayInvoices.Run(brain, CancellationToken.None);

public static class MondayInvoices
{
    public static async Task Run(IDigitalBrain brain, CancellationToken cancellation)
    {
        var week = brain.Get<IMonday>("week");
        var http = brain.Get<IHttp>("http");
        var zip = brain.Get<IZip>("zip");
        var csv = brain.Get<ICsv>("csv");
        var db = brain.Get<IDatabase>("db");

        await foreach (var _ in brain.On<Monday>(week, cancellation))
        {
            var archive = await http.Get(InvoiceZip.Url);
            var files = await zip.Unzip(archive);
            var export = files.Single(file => file.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
            var rows = await csv.Parse(export.Bytes);
            var invoices = rows
                .Where(row => row.Kind.Equals("invoice", StringComparison.OrdinalIgnoreCase))
                .Select(InvoiceZip.Invoice)
                .ToArray();
            await db.Insert("invoices", invoices);
        }
    }
}

public static class DatabaseCheck
{
    public static async Task Run(IDigitalBrain brain, CancellationToken cancellation)
    {
        var db = brain.Get<IDatabase>("db");
        var ui = brain.Get<INotification>("ui");

        await foreach (var added in brain.On<DatabaseAdded>(db, cancellation))
        {
            var stored = await db.Query(added.Table);
            var missing = added.Ids.Except(stored.Select(row => row.Id)).ToArray();
            if (missing.Length == 0)
            {
                await ui.Notify($"{added.Table} has {string.Join(",", added.Ids)}");
            }
        }
    }
}
