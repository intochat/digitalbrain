namespace DigitalBrain.AI;

[GenerateSerializer]
[Alias("ai.orchestration-refused")]
public sealed class OrchestrationRefusedException : Exception
{
    public OrchestrationRefusedException()
        : this("An AI orchestration refused the request.")
    {
    }

    public OrchestrationRefusedException(string message)
        : base(message)
    {
    }

    public OrchestrationRefusedException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
