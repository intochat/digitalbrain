namespace DigitalBrain.Behaviors;

public sealed class BehaviorHostException : Exception
{
    public BehaviorHostException()
        : this("behavior-host-error")
    {
    }

    public BehaviorHostException(string reason)
        : base(reason)
    {
        Reason = reason;
    }

    public BehaviorHostException(string reason, Exception innerException)
        : base(reason, innerException)
    {
        Reason = reason;
    }

    public string Reason { get; } = "behavior-host-error";
}
