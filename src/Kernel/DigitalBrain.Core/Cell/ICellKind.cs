using DigitalBrain.Abstractions;
namespace DigitalBrain.Core;

public interface ICellKind
{
    string Name { get; }

    CellState Apply(CellState state, string key);
}
