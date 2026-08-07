using System.Diagnostics;

namespace DigitalBrain.Core;

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

    internal static void RetractionUncommitted(DigitalBrain.Abstractions.NeuronId neuron, Exception uncommitted)
    {
        using var retraction = Source.StartActivity("db.retraction-uncommitted");

        retraction?.SetStatus(ActivityStatusCode.Error, uncommitted.Message);
        retraction?.SetTag(ReceiverTag, neuron.ToString());
        retraction?.SetTag("db.retraction-uncommitted", uncommitted.GetType().Name);
    }
}
