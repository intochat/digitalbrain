using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Sheet;

[Alias("sheet"), Orleans.Metadata.DefaultGrainType(UIVocabulary.SheetType)]
public interface ISheet : INeuron
{
    Task Set(string title, IReadOnlyList<SheetCell> cells);
    [ReadOnly, Alias("read")] Task<SheetState> Read();
}

[GenerateSerializer, Alias("ui.sheet-cell")]
public sealed record SheetCell(
    [property: Id(0)] int Row,
    [property: Id(1)] int Column,
    [property: Id(2)] string Value);

[GenerateSerializer, Alias("ui.sheet-state")]
public sealed class SheetState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string Title { get; set; } = "";
    [Id(3)] public List<SheetCell> Cells { get; set; } = [];
}
