namespace DigitalBrain.Client;

public sealed class ScriptRefusalException(string message, string code) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
