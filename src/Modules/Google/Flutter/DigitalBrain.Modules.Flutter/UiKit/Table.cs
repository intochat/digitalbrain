using DigitalBrain.Flutter.Table;

namespace DigitalBrain.Flutter;

internal sealed record TableReplace(string Title, IReadOnlyList<TableColumn> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

internal sealed record TableView(string Sort, string Filter);
