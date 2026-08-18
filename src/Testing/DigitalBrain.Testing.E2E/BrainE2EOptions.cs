namespace DigitalBrain.Testing.E2E;

public sealed class BrainE2EOptions
{
    public string[] Args { get; init; } = [];

    // "flutter" mirrors ShellHostingExtensions.DefaultFlutterResourceName (the UI hosting
    // project is not referenced here, so the literal is duplicated rather than linked).
    public string[] ExplicitStart { get; init; } = ["ollama", "openwebui", "flutter"];

    public string[] ExpectedHealthy { get; init; } = ["kernel", "mcp"];

    public TimeSpan HealthTimeout { get; init; } = TimeSpan.FromMinutes(5);
}
