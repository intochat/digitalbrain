using System.Diagnostics;

namespace DigitalBrain.Kernel;

public static class SynapseTelemetry
{
    public const string ActivitySourceName = "DigitalBrain";

    public const string ReceiverTag = "db.receiver";

    public const string SynapseTag = "db.synapse";

    public const string CorrelationTag = "db.correlation";

    public const string WatcherDroppedTag = "db.watcher-dropped";

    internal static readonly ActivitySource Source = new(ActivitySourceName);

    internal static void WatcherDropped(DigitalBrain.Abstractions.NeuronId watched, Exception unreachable)
    {
        using var dropped = Source.StartActivity("db.watcher-dropped");

        dropped?.SetTag(ReceiverTag, watched.ToString());
        dropped?.SetTag(WatcherDroppedTag, unreachable.GetType().Name);
    }
}
