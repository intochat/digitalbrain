namespace DigitalBrain.Testing;

public sealed record TestExecutionOptions
{
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromMinutes(3);
    public TimeSpan AssertionTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public Action<TestDiagnostic>? Diagnostics { get; init; }
    public string? ArtifactDirectory { get; init; }

    public void Validate()
    {
        foreach (var timeout in new[] { StartupTimeout, AssertionTimeout, CleanupTimeout })
        {
            if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
                { throw new ArgumentOutOfRangeException(nameof(timeout), "Test deadlines must be finite and positive."); }
        }
    }
}

public sealed record TestDiagnostic(string Stage, string Message);
