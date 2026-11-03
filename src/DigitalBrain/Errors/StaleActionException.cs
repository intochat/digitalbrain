namespace DigitalBrain.Errors;

// A refusal word: the action named a view generation or node that is no longer current; read the view again.
// It stays an InvalidOperationException so existing handlers keep catching it.
public sealed class StaleActionException(string message) : InvalidOperationException(message);
