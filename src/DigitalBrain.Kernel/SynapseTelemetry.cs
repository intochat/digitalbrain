using System.Diagnostics;

namespace DigitalBrain.Kernel;

public static class SynapseTelemetry
{
    public const string ActivitySourceName = "DigitalBrain";

    public const string ReceiverTag = "db.receiver";

    public const string SynapseTag = "db.synapse";

    public const string CorrelationTag = "db.correlation";

    internal static readonly ActivitySource Source = new(ActivitySourceName);
}
