using System.Diagnostics;

namespace DigitalBrain.Kernel;

internal static class SynapseTelemetry
{
    internal const string ReceiverTag = "db.receiver";

    internal const string SynapseTag = "db.synapse";

    internal const string CorrelationTag = "db.correlation";

    internal static readonly ActivitySource Source = new("DigitalBrain");

    internal static void WatcherDropped(DigitalBrain.Abstractions.NeuronId watched, Exception unreachable)
    {
        using var dropped = Source.StartActivity("db.watcher-dropped");

        dropped?.SetTag(ReceiverTag, watched.ToString());
        dropped?.SetTag("db.watcher-dropped", unreachable.GetType().Name);
    }
}
