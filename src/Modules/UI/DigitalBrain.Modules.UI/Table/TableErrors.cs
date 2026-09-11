namespace DigitalBrain.UI;

public sealed class TableValidationException(string message) : ArgumentException(message);
public sealed class TableNotFoundException(string id) : KeyNotFoundException($"Table '{id}' was not found.");
public sealed class TableRevisionConflictException(string message) : Exception(message);
