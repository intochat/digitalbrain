using DigitalBrain.Abstractions;
namespace DigitalBrain.Core;

internal interface ICellKind
{
    string Name { get; }

    CellState Apply(CellState state, string key);
}
