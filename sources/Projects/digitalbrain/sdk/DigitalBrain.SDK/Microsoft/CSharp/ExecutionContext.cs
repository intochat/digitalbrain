namespace DigitalBrain.SDK.Microsoft.CSharp;

public sealed class ExecutionContext
{
    public Dictionary<string, object> Globals { get; } = new(StringComparer.Ordinal);
    public IServiceProvider Services { get; set; } = null!;
}
