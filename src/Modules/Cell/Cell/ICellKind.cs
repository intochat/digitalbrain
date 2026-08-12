namespace DigitalBrain.Cell;

internal interface ICellKind
{
    string Name { get; }

    CellState Apply(CellState state, string key);
}
