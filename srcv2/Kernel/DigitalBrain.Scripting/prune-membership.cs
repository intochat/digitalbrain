#:package Azure.Data.Tables@12.11.0
#:property ManagePackageVersionsCentrally=false
#:property TreatWarningsAsErrors=false
#:property EnforceCodeStyleInBuild=false

using System.Net.Sockets;
using Azure.Data.Tables;

var clustering = Environment.GetEnvironmentVariable("ConnectionStrings__clustering")
    ?? Environment.GetEnvironmentVariable("CLUSTERING_CONNECTIONSTRING")
    ?? throw new InvalidOperationException("No clustering connection string in environment.");

Console.WriteLine("clustering configured (length " + clustering.Length + ")");
var tables = new TableServiceClient(clustering);
var table = tables.GetTableClient("OrleansSiloInstances");

var removed = 0;
await foreach (var entity in table.QueryAsync<TableEntity>())
{
    if (entity.RowKey.Contains("Version", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"KEEP Version PK={entity.PartitionKey} RK={entity.RowKey}");
        continue;
    }

    string? status = null;
    string? address = null;
    int? proxyPort = null;
    foreach (var pair in entity)
    {
        if (string.Equals(pair.Key, "Status", StringComparison.OrdinalIgnoreCase))
        {
            status = pair.Value?.ToString();
        }
        else if (string.Equals(pair.Key, "Address", StringComparison.OrdinalIgnoreCase)
            || string.Equals(pair.Key, "SiloAddress", StringComparison.OrdinalIgnoreCase))
        {
            address = pair.Value?.ToString();
        }
        else if (string.Equals(pair.Key, "ProxyPort", StringComparison.OrdinalIgnoreCase))
        {
            proxyPort = pair.Value switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var p) => p,
                _ => null,
            };
        }
    }

    Console.WriteLine(
        $"ROW PK={entity.PartitionKey} RK={entity.RowKey} status={status} addr={address} proxy={proxyPort}");

    if (proxyPort is not > 0)
    {
        continue;
    }

    var alive = PortOpen("127.0.0.1", proxyPort.Value);
    if (!alive
        && (status is null
            || status.Contains("Active", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Joining", StringComparison.OrdinalIgnoreCase)))
    {
        await table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, entity.ETag);
        Console.WriteLine($"PRUNED dead silo RK={entity.RowKey} proxy={proxyPort}");
        removed++;
    }
}

Console.WriteLine($"PRUNE_DONE removed={removed}");

static bool PortOpen(string host, int port)
{
    try
    {
        using var client = new TcpClient();
        var task = client.ConnectAsync(host, port);
        return task.Wait(TimeSpan.FromMilliseconds(400)) && client.Connected;
    }
    catch
    {
        return false;
    }
}
