namespace DigitalBrain.Core;

public sealed record BrowserLoginDefinition(
    string Provider,
    string DisplayName,
    string Scheme,
    string LoginPath,
    string CallbackPath,
    string Message)
{
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(10);

    public int Capacity { get; init; } = 128;
}
