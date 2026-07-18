namespace DigitalBrain.SDK.Microsoft.CSharp;

public sealed record ScriptResult(
    bool Ok,
    object? ReturnValue,
    IReadOnlyList<string> Diagnostics,
    Exception? Exception = null);
