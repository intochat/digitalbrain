namespace DigitalBrain.Behaviors.Artifacts;

public sealed class BehaviorArtifactException : IOException
{
    public BehaviorArtifactException()
    {
    }

    public BehaviorArtifactException(string message)
        : base(message)
    {
    }

    public BehaviorArtifactException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
