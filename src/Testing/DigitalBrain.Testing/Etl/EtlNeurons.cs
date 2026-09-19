using System.Globalization;
using System.IO.Compression;
using System.Text;
using DigitalBrain.Contracts;
using DigitalBrain.Core;

namespace DigitalBrain.Testing;

public interface IMonday : INeuron
{
    Task Tick();
}

public interface IHttp : INeuron
{
    Task Seed(string url, byte[] body);
    Task<byte[]> Get(string url);
}

public interface IZip : INeuron
{
    Task<IReadOnlyList<ZipEntry>> Unzip(byte[] archive);
}

public interface ICsv : INeuron
{
    Task<IReadOnlyList<CsvRow>> Parse(byte[] csv);
}

public interface IDatabase : INeuron
{
    Task Insert(string table, IReadOnlyList<InvoiceRow> rows);
    Task<IReadOnlyList<InvoiceRow>> Query(string table);
}

[GenerateSerializer, Alias("etl.monday")]
public sealed record Monday([property: Id(0)] DateTimeOffset When) : Signal;

[GenerateSerializer, Alias("etl.zip-entry")]
public sealed record ZipEntry([property: Id(0)] string Name, [property: Id(1)] byte[] Bytes);

[GenerateSerializer, Alias("etl.csv-row")]
public sealed record CsvRow(
    [property: Id(0)] string Kind,
    [property: Id(1)] string Id,
    [property: Id(2)] string Amount);

[GenerateSerializer, Alias("etl.invoice")]
public sealed record InvoiceRow([property: Id(0)] string Id, [property: Id(1)] decimal Amount);

[GenerateSerializer, Alias("etl.db-added")]
public sealed record DatabaseAdded(
    [property: Id(0)] string Table,
    [property: Id(1)] IReadOnlyList<string> Ids) : Signal;

[GrainType("monday")]
public sealed class Week : Neuron, IMonday
{
    public Task Tick() => Broadcast(new Monday(TimeProvider.System.GetUtcNow()));
}

[GrainType("http")]
public sealed class HttpFiles : Neuron, IHttp
{
    private readonly Dictionary<string, byte[]> _files = [];

    public Task Seed(string url, byte[] body)
    {
        _files[url] = body;
        return Task.CompletedTask;
    }

    public Task<byte[]> Get(string url)
        => Task.FromResult(_files.TryGetValue(url, out var body)
            ? body
            : throw new FileNotFoundException(url));
}

[GrainType("zip")]
public sealed class Zip : Neuron, IZip
{
    public Task<IReadOnlyList<ZipEntry>> Unzip(byte[] archive)
    {
        using var stream = new MemoryStream(archive);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var files = zip.Entries.Select(entry =>
        {
            using var raw = entry.Open();
            using var copy = new MemoryStream();
            raw.CopyTo(copy);
            return new ZipEntry(entry.Name, copy.ToArray());
        }).ToArray();
        return Task.FromResult<IReadOnlyList<ZipEntry>>(files);
    }
}

[GrainType("csv")]
public sealed class Csv : Neuron, ICsv
{
    public Task<IReadOnlyList<CsvRow>> Parse(byte[] csv)
    {
        var text = Encoding.UTF8.GetString(csv);
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var rows = lines.Skip(1).Select(line =>
        {
            var cells = line.Split(',');
            return new CsvRow(cells[0], cells[1], cells[2]);
        }).ToArray();
        return Task.FromResult<IReadOnlyList<CsvRow>>(rows);
    }
}

[GrainType("database")]
public sealed class Database : Neuron, IDatabase
{
    private readonly Dictionary<string, List<InvoiceRow>> _tables = [];

    public async Task Insert(string table, IReadOnlyList<InvoiceRow> rows)
    {
        if (!_tables.TryGetValue(table, out var stored))
        {
            _tables[table] = stored = [];
        }

        var added = new List<InvoiceRow>();
        foreach (var row in rows)
        {
            if (stored.Any(existing => existing.Id == row.Id))
            {
                continue;
            }

            stored.Add(row);
            added.Add(row);
        }

        if (added.Count == 0)
        {
            return;
        }

        await Broadcast(new DatabaseAdded(table, [.. added.Select(row => row.Id)]));
    }

    public Task<IReadOnlyList<InvoiceRow>> Query(string table)
        => Task.FromResult<IReadOnlyList<InvoiceRow>>(
            _tables.TryGetValue(table, out var rows) ? [.. rows] : []);
}

public static class InvoiceZip
{
    public const string Url = "https://files.example.com/monday/invoices.zip";

    public static byte[] Create()
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("export.csv");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write("kind,id,amount\ninvoice,INV-1,100\nnote,N-1,0\ninvoice,INV-2,250\n");
        }

        return archive.ToArray();
    }

    public static InvoiceRow Invoice(CsvRow row)
        => new(row.Id, decimal.Parse(row.Amount, CultureInfo.InvariantCulture));
}
