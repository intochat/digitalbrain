using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Testing;

[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "Every framework failure must carry a bounded BrainTestArtifact and preserve its original exception; a constructor without both would create an invalid diagnostic failure.")]
public sealed class BrainTestFailureException : Exception
{
    internal const string AttachmentName = "digitalbrain-test.json";

    internal BrainTestFailureException(
        string message,
        BrainTestArtifact artifact,
        Exception innerException)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        Artifact = artifact;
    }

    public BrainTestArtifact Artifact { get; }
}
