namespace DigitalBrain.Testing;

public sealed record TestExecutionOptions
{
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromMinutes(3);
    public TimeSpan AssertionTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public Action<TestDiagnostic>? Diagnostics { get; init; }
    public string? ArtifactDirectory { get; init; }
    public IReadOnlyDictionary<string, string?> PrivateConfiguration { get; init; } = new Dictionary<string, string?>();

    public void Validate()
    {
        foreach (var key in PrivateConfiguration.Keys)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Contains("__", StringComparison.Ordinal)
                || key.StartsWith("Orleans:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("DigitalBrain:Testing:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("DigitalBrain:Modules:", StringComparison.OrdinalIgnoreCase))
                { throw new ArgumentException("Private settings cannot override host-owned configuration."); }
        }
        foreach (var timeout in new[] { StartupTimeout, AssertionTimeout, CleanupTimeout })
        {
            ValidateTimeout(timeout);
        }
    }

    public static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
            { throw new ArgumentOutOfRangeException(nameof(timeout), "Test deadlines must be finite and positive."); }
    }
}

public sealed record TestDiagnostic(string Stage, string Message);
