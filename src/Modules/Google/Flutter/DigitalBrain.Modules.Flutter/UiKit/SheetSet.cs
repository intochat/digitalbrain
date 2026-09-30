using DigitalBrain.Flutter.Sheet;

namespace DigitalBrain.Flutter;

internal sealed record SheetSet(string Title, IReadOnlyList<SheetCell> Cells);
