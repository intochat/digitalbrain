using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Table;

[Alias("table"), Orleans.Metadata.DefaultGrainType(UIVocabulary.TableType)]
public interface ITable : INeuron
{
    Task Replace(string title, IReadOnlyList<TableColumn> columns, IReadOnlyList<IReadOnlyList<string>> rows);
    Task SetView(string sort, string filter);
    [ReadOnly, Alias("read")] Task<TableState> Read();
}

[GenerateSerializer, Alias("ui.table-column")]
public sealed record TableColumn([property: Id(0)] string Id, [property: Id(1)] string Title);

[GenerateSerializer, Alias("ui.table-state")]
public sealed class TableState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string Title { get; set; } = "";
    [Id(3)] public List<TableColumn> Columns { get; set; } = [];
    [Id(4)] public List<List<string>> Rows { get; set; } = [];
    [Id(5)] public string Sort { get; set; } = "";
    [Id(6)] public string Filter { get; set; } = "";
}
