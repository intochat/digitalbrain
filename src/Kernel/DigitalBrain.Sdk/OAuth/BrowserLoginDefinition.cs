namespace DigitalBrain.Sdk;

// Provider identity for one browser login rail: the UserActionRequest fields the UI shows, the
// kernel paths the provider's OAuth client must have registered, and the authentication scheme
// the login path challenges.
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
