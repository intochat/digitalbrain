using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Testing;

[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "Every AppHost failure must carry a finalized bounded artifact and preserve its original exception.")]
public sealed class AppHostTestFailureException : InvalidOperationException
{
    internal const string AttachmentName = "digitalbrain-apphost.json";

    internal AppHostTestFailureException(
        string message,
        AppHostTestArtifact artifact,
        Exception innerException)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        Artifact = artifact;
    }

    public AppHostTestArtifact Artifact { get; }
}
