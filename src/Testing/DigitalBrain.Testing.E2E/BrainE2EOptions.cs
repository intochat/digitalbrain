namespace DigitalBrain.Testing.E2E;

public sealed class BrainE2EOptions
{
    public string[] Args { get; init; } = [];

    // "flutter" mirrors ShellHostingExtensions.DefaultFlutterResourceName (the UI hosting
    // project is not referenced here, so the literal is duplicated rather than linked).
    public string[] ExplicitStart { get; init; } = ["ollama", "openwebui", "flutter"];

    public string[] ExpectedHealthy { get; init; } = ["kernel", "mcp"];

    public TimeSpan HealthTimeout { get; init; } = TimeSpan.FromMinutes(5);

    // Keyed by ParameterResource.Name. BrainAppHostFixture.StubParameters checks this before its
    // own shape-aware defaults, so a caller can override any parameter (including the
    // state-protection key) without subclassing the fixture.
    public Dictionary<string, string> ParameterOverrides { get; init; } = new(StringComparer.Ordinal);

    // Environment stamped on every project resource (kernel, mcp) before boot — the corpus
    // path and any per-run test knobs ride here, applied by the same ArmProjectResources loop
    // that stamps the Testing mode.
    public Dictionary<string, string> ProjectEnvironment { get; init; } = new(StringComparer.Ordinal);
}
