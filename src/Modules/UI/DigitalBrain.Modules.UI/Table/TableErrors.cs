namespace DigitalBrain.UI;

public sealed class TableValidationException(string message) : ArgumentException(message);
public sealed class TableNotFoundException(string id) : KeyNotFoundException($"Table '{id}' was not found.");
public sealed class TableRevisionConflictException(string message) : Exception(message);

// Raised when the neuron behind a table cannot serve rows (for example the query behind a live
// table was refused by its database). Serializable so it crosses the grain boundary intact.
[GenerateSerializer, Alias("ui.table-source-failed")]
public sealed class TableSourceException(string message) : InvalidOperationException(message);
