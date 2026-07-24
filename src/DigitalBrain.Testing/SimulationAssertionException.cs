namespace DigitalBrain.Testing;

public sealed class SimulationAssertionException : Exception
{
    public SimulationAssertionException()
        : this("A simulation expectation was not met.")
    {
    }

    public SimulationAssertionException(string message)
        : this(message, artifact: null, innerException: null)
    {
    }

    public SimulationAssertionException(string message, Exception innerException)
        : this(message, artifact: null, innerException)
    {
    }

    public SimulationAssertionException(string message, ScenarioFailureArtifact? artifact)
        : this(message, artifact, innerException: null)
    {
    }

    public SimulationAssertionException(string message, ScenarioFailureArtifact? artifact, Exception? innerException)
        : base(FormatMessage(message, artifact), innerException)
    {
        Artifact = artifact;
        if (artifact is not null)
        {
            Data["ScenarioFailureArtifact"] = artifact.ToString();
        }
    }

    public ScenarioFailureArtifact? Artifact { get; }

    private static string FormatMessage(string message, ScenarioFailureArtifact? artifact)
        => artifact is null
            ? message
            : $"{message}{Environment.NewLine}{artifact}";
}
